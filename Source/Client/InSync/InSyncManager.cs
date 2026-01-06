using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using HybridShared.Packets;

namespace HybridClient.InSync
{
    /// <summary>
    /// InSync 세션 관리자
    /// 클라이언트 측에서 InSync 상태를 관리하고 패킷을 처리
    /// </summary>
    public class InSyncManager
    {
        private static InSyncManager _instance;
        public static InSyncManager Instance => _instance ??= new InSyncManager();
        
        /// <summary>현재 InSync 세션 ID</summary>
        public int CurrentSessionId { get; private set; } = -1;
        
        /// <summary>상대 유저네임</summary>
        public string PartnerUsername { get; private set; }
        
        /// <summary>현재 역할</summary>
        public InSyncRole Role { get; private set; } = InSyncRole.None;
        
        /// <summary>InSync 모드</summary>
        public InSyncMode Mode { get; private set; }
        
        /// <summary>동기화 대상 맵</summary>
        public Map SyncMap { get; private set; }
        
        /// <summary>현재 동기화 틱</summary>
        public int SyncTick { get; private set; }
        
        /// <summary>InSync 활성 여부</summary>
        public bool IsActive => CurrentSessionId >= 0 && Role != InSyncRole.None;
        
        /// <summary>InSync 요청 중인 캐러밴</summary>
        private Caravan pendingCaravan;
        
        /// <summary>타겟 정착지 타일</summary>
        private int targetTileId = -1;
        
        // ========== 패킷 핸들러 ==========
        
        /// <summary>
        /// 서버에서 받은 InSync 응답 처리 (침입자)
        /// </summary>
        public void HandleInSyncResponse(InSyncResponsePacket packet)
        {
            Log.Message($"[HybridMP][INSYNC] Received response: {packet.Response}, Session: {packet.SessionId}");
            
            if (packet.Response == InSyncResponse.Accepted)
            {
                CurrentSessionId = packet.SessionId;
                Role = InSyncRole.Invader;
                LockstepController.Instance.SetRequesting();
                
                Messages.Message("InSync request accepted. Waiting for map data...", MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                Messages.Message($"InSync request rejected: {packet.Response}", MessageTypeDefOf.NegativeEvent, false);
                Reset();
            }
        }
        
        /// <summary>
        /// 서버에서 받은 InSync 알림 처리 (권위자)
        /// insync_design.md: 권위자는 침입자 폰 스폰 후 스냅샷 전송
        /// </summary>
        public void HandleInSyncNotify(InSyncNotifyPacket packet)
        {
            Log.Message($"[HybridMP][INSYNC] Received notify: {packet.RequesterUsername} wants to enter at tile {packet.TileId}");
            
            CurrentSessionId = packet.SessionId;
            PartnerUsername = packet.RequesterUsername;
            Role = InSyncRole.Authority;
            Mode = packet.Mode;
            
            // 해당 타일의 맵 찾기
            foreach (var map in Find.Maps)
            {
                if (map.Tile == packet.TileId)
                {
                    SyncMap = map;
                    break;
                }
            }
            
            if (SyncMap == null)
            {
                Log.Error($"[HybridMP][INSYNC] Map not found for tile {packet.TileId}");
                Reset();
                return;
            }
            
            // ===== 상태 보존 (MP SaveLoad 패턴) =====
            InSyncStatePreserver.SaveState(SyncMap);
            
            // ===== FactionMapData 초기화 (MP 패턴) =====
            InSyncFactionMapManager.InitializeForMap(SyncMap);
            
            // ===== 권위자 세력 초기화 =====
            InSyncFactionManager.InitializeForInSync(true, packet.RequesterUsername);
            
            // ===== 침입자 세력용 FactionMapData 생성 =====
            if (InSyncFactionManager.InvaderFaction != null)
            {
                InSyncFactionMapManager.CreateForFaction(SyncMap, InSyncFactionManager.InvaderFaction);
            }
            
            // ===== 침입자 폰 스폰 (스냅샷 전송 전에!) =====
            if (packet.Pawns != null && packet.Pawns.Count > 0)
            {
                SpawnInvaderPawnsFromPacket(SyncMap, packet.Pawns);
            }
            else
            {
                Log.Warning("[HybridMP][INSYNC] No invader pawns in packet");
            }
            
            // 맵 스냅샷 생성 및 전송 (폰 스폰 후)
            SendMapSnapshot();
            
            // ===== 명령 동기화 시작 =====
            SyncHandler.Instance.StartCapturing();
            
            // Lockstep 진입
            LockstepController.Instance.EnterAsAuthority(
                packet.SessionId,
                packet.RequesterUsername,
                SyncMap
            );
            
            // 카메라를 해당 맵으로 전환
            Current.Game.CurrentMap = SyncMap;
            
            Messages.Message($"{packet.RequesterUsername} is entering your settlement!", MessageTypeDefOf.NeutralEvent, false);
        }
        
        /// <summary>
        /// 맵 스냅샷 패킷 처리 (침입자)
        /// </summary>
        public void HandleMapSnapshot(MapSnapshotPacket packet)
        {
            Log.Message($"[HybridMP][INSYNC] Received map snapshot: Session {packet.SessionId}, Tick {packet.CurrentTick}");
            
            if (packet.SessionId != CurrentSessionId)
            {
                Log.Warning($"[HybridMP][INSYNC] Session mismatch: expected {CurrentSessionId}, got {packet.SessionId}");
                return;
            }
            
            LockstepController.Instance.SetLoading();
            
            // ===== 침입자 폰 정보 저장 (게임 로드 전) =====
            List<PawnInfo> invaderPawnInfos = new List<PawnInfo>();
            if (pendingCaravan != null && !pendingCaravan.Destroyed)
            {
                foreach (var pawn in pendingCaravan.PawnsListForReading)
                {
                    invaderPawnInfos.Add(new PawnInfo
                    {
                        Name = pawn.Name?.ToStringFull ?? "Unknown",
                        KindDef = pawn.kindDef?.defName ?? "Colonist",
                        FactionDef = pawn.Faction?.def?.defName ?? "PlayerColony"
                    });
                }
                Log.Message($"[HybridMP][INSYNC] Saved {invaderPawnInfos.Count} invader pawns before loading");
            }
            
            // 권위자의 전체 게임을 로드
            bool success = MapSnapshotManager.LoadSnapshot(packet, out int mapId, out int startTick);
            
            if (!success)
            {
                Log.Error("[HybridMP][INSYNC] Failed to load snapshot");
                Reset();
                return;
            }
            
            SyncTick = packet.CurrentTick;
            
            // 게임 로드 완료 후 처리
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    // 로드된 게임에서 맵 찾기
                    SyncMap = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId) ?? Find.CurrentMap;
                    
                    if (SyncMap == null && Find.Maps.Count > 0)
                    {
                        SyncMap = Find.Maps[0];
                    }
                    
                    if (SyncMap != null)
                    {
                        Current.Game.CurrentMap = SyncMap;
                        
                        // ===== FactionMapData 초기화 (MP 패턴) =====
                        InSyncFactionMapManager.InitializeForMap(SyncMap);
                        
                        // ===== 세력 분리 설정 =====
                        InSyncFactionManager.SetupInvaderFactionAfterLoad(NetworkManager.Instance?.Username ?? "Invader");
                        
                        // ===== 침입자 세력용 FactionMapData 생성 =====
                        if (InSyncFactionManager.InvaderFaction != null)
                        {
                            InSyncFactionMapManager.CreateForFaction(SyncMap, InSyncFactionManager.InvaderFaction);
                            InSyncFactionMapManager.SwapToFaction(SyncMap, InSyncFactionManager.InvaderFaction);
                        }
                        
                        // ===== 침입자 폰 스폰 (별도 세력으로) =====
                        SpawnInvaderPawns(SyncMap, invaderPawnInfos);
                        
                        // ===== 상태 복원 (MP SaveLoad 패턴) =====
                        InSyncStatePreserver.RestoreState(SyncMap);
                        
                        // ===== 명령 동기화 시작 =====
                        SyncHandler.Instance.StartCapturing();
                        
                        // Lockstep 진입
                        LockstepController.Instance.EnterAsInvader(
                            packet.SessionId,
                            PartnerUsername,
                            SyncMap,
                            packet.CurrentTick,
                            packet.RandState
                        );
                        
                        Messages.Message("Entered settlement successfully!", MessageTypeDefOf.PositiveEvent, false);
                        Log.Message($"[HybridMP][INSYNC] Entry complete - Map {SyncMap.uniqueID}");
                    }
                    else
                    {
                        Log.Error("[HybridMP][INSYNC] No map found after loading");
                        Reset();
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"[HybridMP][INSYNC] Failed to enter map: {e}");
                    Reset();
                }
            });
        }
        
        /// <summary>
        /// 침입자 폰 정보 저장용 클래스
        /// </summary>
        private class PawnInfo
        {
            public string Name;
            public string KindDef;
            public string FactionDef;
        }
        
        /// <summary>
        /// 침입자 폰을 권위자 맵에 스폰
        /// </summary>
        private void SpawnInvaderPawns(Map map, List<PawnInfo> pawnInfos)
        {
            if (map == null)
            {
                Log.Error("[HybridMP][INSYNC] Cannot spawn pawns - map is null");
                return;
            }
            
            if (pawnInfos == null || pawnInfos.Count == 0)
            {
                Log.Warning("[HybridMP][INSYNC] No invader pawns to spawn");
                return;
            }
            
            Log.Message($"[HybridMP][INSYNC] Attempting to spawn {pawnInfos.Count} invader pawns");
            
            // 침입자 Faction 가져오기 (InSyncFactionManager에서 생성한 세력 사용)
            Faction invaderFaction = InSyncFactionManager.InvaderFaction ?? InSyncFactionManager.MyFaction;
            if (invaderFaction == null)
            {
                // 폴백: 기존 플레이어 세력 사용
                invaderFaction = Find.FactionManager?.AllFactions?.FirstOrDefault(f => f.IsPlayer);
            }
            if (invaderFaction == null)
            {
                Log.Error("[HybridMP][INSYNC] No faction found for invader pawns");
                return;
            }
            Log.Message($"[HybridMP][INSYNC] Using faction for invader pawns: {invaderFaction.Name}");
            
            // 맵 가장자리에서 스폰 위치 계산 (PathGrid 미초기화 상태에서도 동작)
            // 맵 크기 기반으로 가장자리 위치 계산
            IntVec3 spawnCenter;
            int mapWidth = map.Size.x;
            int mapHeight = map.Size.z;
            int edge = Rand.Range(0, 4); // 0=북, 1=남, 2=동, 3=서
            
            switch (edge)
            {
                case 0: // 북쪽 가장자리
                    spawnCenter = new IntVec3(Rand.Range(10, mapWidth - 10), 0, mapHeight - 5);
                    break;
                case 1: // 남쪽 가장자리
                    spawnCenter = new IntVec3(Rand.Range(10, mapWidth - 10), 0, 5);
                    break;
                case 2: // 동쪽 가장자리
                    spawnCenter = new IntVec3(mapWidth - 5, 0, Rand.Range(10, mapHeight - 10));
                    break;
                default: // 서쪽 가장자리
                    spawnCenter = new IntVec3(5, 0, Rand.Range(10, mapHeight - 10));
                    break;
            }
            
            Log.Message($"[HybridMP][INSYNC] Spawn center: {spawnCenter}");
            
            int spawned = 0;
            foreach (var info in pawnInfos)
            {
                if (info == null)
                {
                    Log.Warning("[HybridMP][INSYNC] Skipping null pawn info");
                    continue;
                }
                
                try
                {
                    // PawnKindDef 찾기
                    var kindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(info.KindDef);
                    if (kindDef == null)
                    {
                        kindDef = PawnKindDefOf.Colonist;
                        Log.Warning($"[HybridMP][INSYNC] KindDef '{info.KindDef}' not found, using Colonist");
                    }
                    
                    // 폰 생성
                    var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        kindDef,
                        invaderFaction,
                        PawnGenerationContext.NonPlayer
                    ));
                    
                    if (pawn == null)
                    {
                        Log.Error("[HybridMP][INSYNC] PawnGenerator returned null");
                        continue;
                    }
                    
                    // 이름 설정
                    if (!string.IsNullOrEmpty(info.Name) && info.Name != "Unknown")
                    {
                        pawn.Name = new NameSingle(info.Name);
                    }
                    
                    // 스폰 위치 (중앙에서 약간 떨어진 위치)
                    IntVec3 spawnPos = new IntVec3(
                        spawnCenter.x + Rand.Range(-3, 4),
                        0,
                        spawnCenter.z + Rand.Range(-3, 4)
                    );
                    
                    // 맵 범위 내로 클램핑
                    if (spawnPos.x < 1) spawnPos.x = 1;
                    if (spawnPos.x > mapWidth - 2) spawnPos.x = mapWidth - 2;
                    if (spawnPos.z < 1) spawnPos.z = 1;
                    if (spawnPos.z > mapHeight - 2) spawnPos.z = mapHeight - 2;
                    
                    // 스폰
                    GenSpawn.Spawn(pawn, spawnPos, map, WipeMode.Vanish);
                    
                    // 첫 번째 폰으로 카메라 점프 및 선택
                    if (spawned == 0)
                    {
                        CameraJumper.TryJump(pawn);
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(pawn);
                        Log.Message($"[HybridMP][INSYNC] Camera jumped to first pawn: {pawn.Name}");
                    }
                    
                    spawned++;
                    Log.Message($"[HybridMP][INSYNC] Spawned invader pawn: {pawn.Name?.ToStringFull} at {spawnPos}");
                }
                catch (Exception e)
                {
                    Log.Error($"[HybridMP][INSYNC] Failed to spawn pawn: {e}");
                }
            }
            
            Log.Message($"[HybridMP][INSYNC] Spawned {spawned}/{pawnInfos.Count} invader pawns");
        }
        
        /// <summary>
        /// 패킷의 PawnInfo를 사용하여 침입자 폰 스폰 (권위자 측)
        /// </summary>
        private void SpawnInvaderPawnsFromPacket(Map map, List<HybridShared.Packets.PawnInfo> pawnInfos)
        {
            if (map == null || pawnInfos == null || pawnInfos.Count == 0)
            {
                Log.Warning("[HybridMP][INSYNC] SpawnInvaderPawnsFromPacket - invalid parameters");
                return;
            }
            
            // 패킷 PawnInfo를 내부 PawnInfo로 변환
            var internalInfos = new List<PawnInfo>();
            foreach (var p in pawnInfos)
            {
                internalInfos.Add(new PawnInfo
                {
                    Name = p.Name,
                    KindDef = p.KindDef,
                    FactionDef = p.FactionDef
                });
            }
            
            // 기존 메서드 호출
            SpawnInvaderPawns(map, internalInfos);
        }
        
        /// <summary>
        /// InSync 종료 패킷 처리
        /// </summary>
        public void HandleInSyncEnd(InSyncEndPacket packet)
        {
            if (packet.SessionId != CurrentSessionId)
                return;
            
            Log.Message($"[HybridMP][INSYNC] Session ended: {packet.Reason}");
            
            LockstepController.Instance.ExitLockstep(packet.Reason);
            Reset();
            
            Messages.Message($"InSync ended: {packet.Reason}", MessageTypeDefOf.NeutralEvent, false);
        }
        
        /// <summary>
        /// Lockstep 명령 패킷 처리 (상대방의 명령 수신)
        /// </summary>
        public void HandleLockstepCommand(LockstepCommandPacket packet)
        {
            if (packet.SessionId != CurrentSessionId)
                return;
            
            Log.Message($"[HybridMP][INSYNC] Received command from partner for tick {packet.ExecuteTick}");
            
            // 명령을 CommandQueue에 추가
            CommandQueue.Instance.Enqueue(packet);
        }
        
        // ========== InSync 시작 ==========
        
        /// <summary>
        /// InSync 요청 설정 (CaravanGizmoPatch에서 호출)
        /// </summary>
        public void SetPendingRequest(Caravan caravan, int tileId)
        {
            pendingCaravan = caravan;
            targetTileId = tileId;
        }
        
        // ========== 내부 메서드 ==========
        
        /// <summary>
        /// 맵 스냅샷 전송 (권위자)
        /// </summary>
        private void SendMapSnapshot()
        {
            if (SyncMap == null)
            {
                Log.Error("[HybridMP][INSYNC] Cannot send snapshot: no map");
                return;
            }
            
            Log.Message($"[HybridMP][INSYNC] Sending map snapshot for session {CurrentSessionId}");
            
            var packet = MapSnapshotManager.CreateSnapshot(SyncMap, CurrentSessionId);
            if (packet != null)
            {
                NetworkManager.Instance.Send(packet);
            }
        }
        
        /// <summary>
        /// 타겟 맵 진입 (침입자)
        /// 캐러밴을 정착지 맵에 진입시킴
        /// </summary>
        private void EnterTargetMap(MapSnapshotPacket packet)
        {
            Log.Message($"[HybridMP][INSYNC] Entering target map at tile {targetTileId}");
            
            // 대기 중인 캐러밴 확인
            if (pendingCaravan == null || pendingCaravan.Destroyed)
            {
                Log.Error("[HybridMP][INSYNC] No valid pending caravan");
                return;
            }
            
            // 정착지 찾기
            Settlement settlement = null;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is Settlement s && s.Tile == targetTileId)
                {
                    settlement = s;
                    break;
                }
            }
            
            if (settlement == null)
            {
                Log.Error($"[HybridMP][INSYNC] Settlement not found at tile {targetTileId}");
                return;
            }
            
            // 맵 가져오기 또는 생성
            Map map = settlement.Map;
            if (map == null)
            {
                Log.Message($"[HybridMP][INSYNC] Generating map for settlement at tile {targetTileId}");
                map = GetOrGenerateMapUtility.GetOrGenerateMap(targetTileId, null);
            }
            
            if (map == null)
            {
                Log.Error("[HybridMP][INSYNC] Failed to get or generate map");
                return;
            }
            
            SyncMap = map;
            
            // 캐러밴 진입
            Log.Message($"[HybridMP][INSYNC] Entering caravan into map");
            
            // 캐러밴의 폰들을 맵에 스폰
            var pawns = pendingCaravan.PawnsListForReading.ToList();
            IntVec3 spawnCell = FindSpawnLocation(map);
            
            foreach (var pawn in pawns)
            {
                if (pawn.holdingOwner != null)
                {
                    pawn.holdingOwner.Remove(pawn);
                }
            }
            
            // 캐러밴 제거
            pendingCaravan.Destroy();
            
            // 폰 스폰
            foreach (var pawn in pawns)
            {
                GenSpawn.Spawn(pawn, spawnCell, map, WipeMode.Vanish);
                spawnCell = CellFinder.RandomClosewalkCellNear(spawnCell, map, 5);
            }
            
            // 카메라 전환
            Current.Game.CurrentMap = map;
            
            // 첫 폰 선택 및 카메라 이동
            if (pawns.Count > 0)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawns[0]);
                CameraJumper.TryJumpAndSelect(pawns[0]);
            }
            
            Log.Message($"[HybridMP][INSYNC] Entry complete - {pawns.Count} pawns spawned");
            
            pendingCaravan = null;
        }
        
        /// <summary>
        /// 맵 가장자리에서 스폰 위치 찾기
        /// </summary>
        private IntVec3 FindSpawnLocation(Map map)
        {
            // 맵 가장자리에서 유효한 위치 찾기
            if (CellFinder.TryFindRandomEdgeCellWith(
                c => c.Standable(map) && !c.Fogged(map),
                map,
                CellFinder.EdgeRoadChance_Neutral,
                out IntVec3 result))
            {
                return result;
            }
            
            // 실패시 맵 중앙 근처
            return map.Center;
        }
        
        /// <summary>
        /// 명령 전송 (양측이 사용)
        /// </summary>
        public void SendCommand(byte commandType, byte[] commandData)
        {
            if (!IsActive)
                return;
            
            var packet = new LockstepCommandPacket
            {
                SessionId = CurrentSessionId,
                ExecuteTick = SyncTick + 1, // 다음 틱에서 실행
                SenderUsername = NetworkManager.Instance.Username,
                CommandType = commandType
            };
            packet.SetCommandData(commandData);
            
            NetworkManager.Instance.Send(packet);
            
            // 로컬에도 추가
            CommandQueue.Instance.Enqueue(packet);
        }
        
        /// <summary>
        /// InSync 종료 요청
        /// </summary>
        public void RequestEnd(string reason)
        {
            if (!IsActive)
                return;
            
            var packet = new InSyncEndPacket
            {
                SessionId = CurrentSessionId,
                Reason = reason
            };
            NetworkManager.Instance.Send(packet);
            
            LockstepController.Instance.ExitLockstep(reason);
            Reset();
        }
        
        /// <summary>
        /// 상태 초기화
        /// </summary>
        public void Reset()
        {
            // ===== MP 패턴 정리 =====
            if (SyncMap != null)
            {
                InSyncFactionMapManager.Cleanup(SyncMap);
            }
            InSyncStatePreserver.Cleanup();
            
            CurrentSessionId = -1;
            PartnerUsername = null;
            Role = InSyncRole.None;
            SyncMap = null;
            SyncTick = 0;
            pendingCaravan = null;
            targetTileId = -1;
            
            CommandQueue.Instance.Clear();
        }
        
        /// <summary>
        /// 틱 진행 (Lockstep 모드에서 호출)
        /// </summary>
        public void Tick()
        {
            if (!IsActive || !LockstepController.Instance.IsInLockstep)
                return;
            
            // 현재 틱의 명령 실행
            CommandQueue.Instance.ExecuteForTick(SyncTick);
            
            SyncTick++;
        }
    }
}
