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
            
            // 맵 스냅샷 생성 및 전송
            SendMapSnapshot();
            
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
                        
                        // ===== 침입자 폰 스폰 =====
                        SpawnInvaderPawns(SyncMap, invaderPawnInfos);
                        
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
            
            // 침입자 Faction 생성 또는 가져오기
            Faction invaderFaction = Find.FactionManager?.AllFactions?.FirstOrDefault(f => f.IsPlayer);
            if (invaderFaction == null)
            {
                Log.Error("[HybridMP][INSYNC] No player faction found");
                return;
            }
            
            // 맵 가장자리에서 안전한 스폰 위치 찾기
            IntVec3 spawnCenter = IntVec3.Invalid;
            try
            {
                // 먼저 RandomEdgeCell 시도
                if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Neutral, out spawnCenter))
                {
                    // 실패하면 맵 중앙 근처에서 스폰
                    spawnCenter = map.Center;
                    Log.Warning("[HybridMP][INSYNC] Could not find edge cell, using map center");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[HybridMP][INSYNC] Error finding spawn location: {e.Message}. Using map center.");
                spawnCenter = map.Center;
            }
            
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
                        PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true
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
                    
                    // 안전한 스폰 위치 찾기
                    IntVec3 spawnPos = spawnCenter;
                    if (!CellFinder.TryFindRandomCellNear(spawnCenter, map, 10, c => c.Standable(map), out spawnPos))
                    {
                        spawnPos = spawnCenter;
                    }
                    
                    // 스폰
                    GenSpawn.Spawn(pawn, spawnPos, map, WipeMode.Vanish);
                    
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
        /// Lockstep 명령 패킷 처리
        /// </summary>
        public void HandleLockstepCommand(LockstepCommandPacket packet)
        {
            if (packet.SessionId != CurrentSessionId)
                return;
            
            Log.Message($"[HybridMP][INSYNC] Received command: Type {packet.CommandType}, Tick {packet.ExecuteTick}");
            
            // 명령 큐에 추가
            CommandQueue.Instance.Enqueue(packet);
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
