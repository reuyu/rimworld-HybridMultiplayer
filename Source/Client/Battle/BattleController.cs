using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using HybridShared;
using HybridShared.Packets;

namespace HybridClient.Battle
{
    /// <summary>
    /// 전투 컨트롤러 - 클라이언트 측 전투 상태 관리.
    /// 서버 틱에 맞춰 동기화하고, Desync 시 Fast Resync 적용.
    /// </summary>
    public class BattleController
    {
        private static BattleController _instance;
        public static BattleController Instance => _instance ??= new BattleController();
        
        /// <summary>현재 전투 ID</summary>
        public string CurrentBattleId { get; private set; }
        
        /// <summary>전투 상태</summary>
        public BattleState State { get; private set; } = BattleState.None;
        
        /// <summary>현재 틱</summary>
        public int CurrentTick { get; private set; }
        
        /// <summary>전투 중인지 여부</summary>
        public static bool IsInBattle => Instance?.State == BattleState.Running;
        
        /// <summary>난수 시드</summary>
        private int randomSeed;
        
        /// <summary>전투 맵</summary>
        private Map battleMap;
        
        /// <summary>대기 중인 액션</summary>
        private Queue<ScheduledAction> pendingActions = new();
        
        /// <summary>네트워크 매니저 참조</summary>
        private NetworkManager network;
        
        /// <summary>로컬 플레이어 ID</summary>
        private int localPlayerId;
        
        /// <summary>마지막 해시 보고 틱</summary>
        private int lastHashReportTick;
        
        /// <summary>해시 보고 간격</summary>
        private const int HashReportInterval = 10;
        
        // 이벤트
        public event Action<BattleStartPacket> OnBattleStarted;
        public event Action<BattleEndPacket> OnBattleEnded;
        public event Action<int, List<ScheduledAction>> OnTickExecuted;
        public event Action<DesyncEventArgs> OnDesyncDetected;
        
        private BattleController() { }
        
        /// <summary>초기화</summary>
        public void Initialize(NetworkManager networkManager, int playerId)
        {
            network = networkManager;
            localPlayerId = playerId;
            
            HybridLogger.Log(LogCategory.Battle, 
                "BattleController initialized", 
                $"PlayerId: {playerId}");
        }
        
        #region Battle Lifecycle
        
        /// <summary>전투 시작</summary>
        public void StartBattle(BattleStartPacket packet)
        {
            if (State != BattleState.None)
            {
                HybridLogger.Warn(LogCategory.Battle, 
                    $"Cannot start: already in state {State}");
                return;
            }
            
            CurrentBattleId = packet.BattleId;
            randomSeed = packet.RandomSeed;
            CurrentTick = packet.StartTick;
            State = BattleState.Loading;
            
            HybridLogger.Log(LogCategory.Battle, 
                "Battle STARTING!", 
                $"BattleId: {CurrentBattleId}, Seed: {randomSeed}, StartTick: {CurrentTick}");
            
            // 난수 시드 설정 (랜덤 동기화)
            try
            {
                Rand.PushState(randomSeed);
                HybridLogger.Verbose(LogCategory.Battle, 
                    $"Random seed set: {randomSeed}");
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Battle, 
                    $"Failed to set random seed: {ex.Message}");
            }
            
            // TODO: 맵 데이터 로드
            if (packet.MapData != null && packet.MapData.Length > 0)
            {
                LoadBattleMap(packet.MapData);
            }
            else
            {
                // 현재 맵 사용
                battleMap = Find.CurrentMap;
            }
            
            // 준비 완료 알림
            State = BattleState.Running;
            SendReady();
            
            OnBattleStarted?.Invoke(packet);
        }
        
        /// <summary>전투 종료</summary>
        public void EndBattle(BattleEndPacket packet)
        {
            HybridLogger.Log(LogCategory.Battle, 
                $"Battle ENDED: {packet.Result}", 
                $"BattleId: {packet.BattleId}, Duration: {packet.DurationTicks} ticks");
            
            // 난수 상태 복원
            try
            {
                Rand.PopState();
            }
            catch { /* 무시 */ }
            
            CurrentBattleId = null;
            State = BattleState.None;
            battleMap = null;
            pendingActions.Clear();
            lastHashReportTick = 0;
            
            OnBattleEnded?.Invoke(packet);
        }
        
        #endregion
        
        #region Sync Handling
        
        /// <summary>서버 동기화 패킷 처리</summary>
        public void HandleSync(BattleSyncPacket packet)
        {
            if (packet.BattleId != CurrentBattleId)
            {
                HybridLogger.Warn(LogCategory.Tick, 
                    $"Sync for wrong battle", 
                    $"Expected: {CurrentBattleId}, Got: {packet.BattleId}");
                return;
            }
            
            int targetTick = packet.ServerTick;
            
            HybridLogger.Verbose(LogCategory.Tick, 
                $"Sync received", 
                $"ServerTick: {targetTick}, Actions: {packet.Actions?.Count ?? 0}");
            
            // 틱 따라잡기
            while (CurrentTick < targetTick)
            {
                CurrentTick++;
                var actionsForTick = packet.Actions?
                    .Where(a => a.ExecuteTick == CurrentTick)
                    .ToList() ?? new List<ScheduledAction>();
                
                ExecuteTick(CurrentTick, actionsForTick);
            }
            
            // Desync 체크 (매 N틱마다)
            if (CurrentTick - lastHashReportTick >= HashReportInterval)
            {
                uint localHash = ComputeStateHash();
                
                if (localHash != packet.ServerStateHash)
                {
                    HybridLogger.Warn(LogCategory.Desync, 
                        "Local hash differs from server!", 
                        $"Tick: {CurrentTick}, Local: 0x{localHash:X8}, Server: 0x{packet.ServerStateHash:X8}");
                    
                    OnDesyncDetected?.Invoke(new DesyncEventArgs
                    {
                        Tick = CurrentTick,
                        LocalHash = localHash,
                        ServerHash = packet.ServerStateHash
                    });
                }
                
                // 해시 보고
                ReportStateHash(localHash);
                lastHashReportTick = CurrentTick;
            }
        }
        
        /// <summary>틱 실행</summary>
        private void ExecuteTick(int tick, List<ScheduledAction> actions)
        {
            HybridLogger.Verbose(LogCategory.Tick, 
                $"Executing tick {tick}", 
                $"Actions: {actions.Count}");
            
            foreach (var action in actions)
            {
                ExecuteAction(action);
            }
            
            // TODO: 실제 게임 틱 실행
            // battleMap?.MapPreTick();
            // battleMap?.MapPostTick();
            
            OnTickExecuted?.Invoke(tick, actions);
        }
        
        /// <summary>액션 실행</summary>
        private void ExecuteAction(ScheduledAction action)
        {
            HybridLogger.Verbose(LogCategory.Action, 
                $"Executing: {action}");
            
            if (battleMap == null)
            {
                HybridLogger.Warn(LogCategory.Action, 
                    "Cannot execute action: no battle map");
                return;
            }
            
            try
            {
                switch (action.Type)
                {
                    case ActionType.Draft:
                        ExecuteDraftAction(action);
                        break;
                    case ActionType.Move:
                        ExecuteMoveAction(action);
                        break;
                    case ActionType.Attack:
                    case ActionType.AttackMelee:
                        ExecuteAttackAction(action);
                        break;
                    default:
                        HybridLogger.Verbose(LogCategory.Action, 
                            $"Unhandled action type: {action.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Action, 
                    $"Failed to execute action: {ex.Message}", 
                    action.ToString());
            }
        }
        
        #endregion
        
        #region Action Execution
        
        private void ExecuteDraftAction(ScheduledAction action)
        {
            if (!action.TargetThingId.HasValue) return;
            
            var thing = ThingRegistry.Instance.Get(action.TargetThingId.Value);
            if (thing is Pawn pawn && pawn.drafter != null)
            {
                // ExtraData[0]이 1이면 Draft, 0이면 Undraft
                bool shouldDraft = action.ExtraData?.Length > 0 && action.ExtraData[0] == 1;
                pawn.drafter.Drafted = shouldDraft;
                
                HybridLogger.Verbose(LogCategory.Action, 
                    $"Pawn {pawn.LabelShort} draft: {shouldDraft}");
            }
        }
        
        private void ExecuteMoveAction(ScheduledAction action)
        {
            if (!action.TargetThingId.HasValue || action.TargetPosition == null) return;
            
            var thing = ThingRegistry.Instance.Get(action.TargetThingId.Value);
            if (thing is Pawn pawn)
            {
                var targetPos = new IntVec3(
                    (int)action.TargetPosition[0],
                    (int)action.TargetPosition[1],
                    (int)action.TargetPosition[2]
                );
                
                // TODO: 실제 이동 명령 구현
                // pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, targetPos));
                
                HybridLogger.Verbose(LogCategory.Action, 
                    $"Pawn {pawn.LabelShort} move to {targetPos}");
            }
        }
        
        private void ExecuteAttackAction(ScheduledAction action)
        {
            if (!action.TargetThingId.HasValue) return;
            
            // TODO: 공격 명령 구현
            HybridLogger.Verbose(LogCategory.Action, 
                $"Attack target ThingID: {action.TargetThingId}");
        }
        
        #endregion
        
        #region State Hash
        
        /// <summary>상태 해시 계산</summary>
        public uint ComputeStateHash()
        {
            if (battleMap == null) return 0;
            
            try
            {
                var snapshots = DeltaSyncManager.Instance.CaptureMapState(battleMap);
                int hash = DeltaSyncManager.Instance.ComputeStateHash(snapshots);
                return (uint)hash;
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Desync, 
                    $"Failed to compute state hash: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>상태 해시 서버에 보고</summary>
        private void ReportStateHash(uint hash)
        {
            if (network == null) return;
            
            var packet = new BattleStateHashPacket
            {
                BattleId = CurrentBattleId,
                Tick = CurrentTick,
                StateHash = hash
            };
            
            network.Send(packet);
            
            HybridLogger.Verbose(LogCategory.Desync, 
                $"Hash reported: 0x{hash:X8}", 
                $"Tick: {CurrentTick}");
        }
        
        #endregion
        
        #region Fast Resync
        
        /// <summary>Fast Resync 적용</summary>
        public void ApplyResync(AuthoritativeStatePacket packet)
        {
            HybridLogger.Log(LogCategory.Resync, 
                "Applying Fast Resync", 
                $"ServerTick: {packet.ServerTick}, Corrections: {packet.Corrections?.Count ?? 0}");
            
            // 기술 검증용: 델타 내용 상세 로그
            Log.Message($"[HybridMP][RESYNC] === Delta Sync Details ===");
            
            if (packet.Corrections != null)
            {
                foreach (var corr in packet.Corrections)
                {
                    Log.Message($"[HybridMP][RESYNC] Correction: ThingID={corr.ThingID}, " +
                        $"Type={corr.Type}, Def={corr.Snapshot?.DefName}, " +
                        $"Pos=({corr.Snapshot?.X:F1},{corr.Snapshot?.Z:F1}), " +
                        $"HP={corr.Snapshot?.HitPointsPercent:P0}");
                }
            }
            
            if (packet.OrphanedThingIDs != null && packet.OrphanedThingIDs.Count > 0)
            {
                Log.Message($"[HybridMP][RESYNC] Orphaned Things (to delete): [{string.Join(", ", packet.OrphanedThingIDs)}]");
            }
            
            if (packet.MissingThings != null)
            {
                foreach (var missing in packet.MissingThings)
                {
                    Log.Message($"[HybridMP][RESYNC] Missing Thing (to create): ThingID={missing.ThingID}, " +
                        $"Def={missing.DefName}, Pos=({missing.X:F1},{missing.Z:F1})");
                }
            }
            
            Log.Message($"[HybridMP][RESYNC] ===========================");
            
            // 실제 적용 (맵이 있을 때만)
            if (battleMap == null)
            {
                HybridLogger.Warn(LogCategory.Resync, 
                    "Skipping actual apply: no battle map (tech verification mode)");
                return;
            }
            
            try
            {
                DeltaSyncManager.Instance.ApplyAuthoritativeState(packet, battleMap);
                
                HybridLogger.Log(LogCategory.Resync, 
                    "Fast Resync applied successfully");
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Resync, 
                    $"Failed to apply resync: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Player Actions (Send to Server)
        
        /// <summary>플레이어 액션 전송</summary>
        public void SendAction(ActionType type, int? targetId = null, float[] position = null, byte[] extraData = null)
        {
            if (!IsInBattle)
            {
                HybridLogger.Warn(LogCategory.Action, 
                    "Cannot send action: not in battle");
                return;
            }
            
            var action = new ScheduledAction
            {
                Type = type,
                ExecuteTick = CurrentTick + 2, // 2틱 후 실행
                PlayerId = localPlayerId,
                TargetThingId = targetId,
                TargetPosition = position,
                ExtraData = extraData
            };
            
            HybridLogger.Log(LogCategory.Action, 
                $"Sending action: {action}");
            
            network?.Send(new BattleActionPacket
            {
                BattleId = CurrentBattleId,
                Action = action
            });
        }
        
        /// <summary>준비 완료 전송</summary>
        private void SendReady()
        {
            network?.Send(new BattleReadyPacket
            {
                BattleId = CurrentBattleId,
                IsReady = true
            });
            
            HybridLogger.Log(LogCategory.Battle, 
                "Ready signal sent");
        }
        
        #endregion
        
        #region Helpers
        
        private void LoadBattleMap(byte[] mapData)
        {
            // TODO: 맵 데이터 로드 구현
            HybridLogger.Log(LogCategory.Battle, 
                $"Loading battle map ({mapData.Length} bytes)");
        }
        
        /// <summary>리셋</summary>
        public void Reset()
        {
            CurrentBattleId = null;
            State = BattleState.None;
            CurrentTick = 0;
            battleMap = null;
            pendingActions.Clear();
            lastHashReportTick = 0;
        }
        
        #endregion
    }
    
    /// <summary>전투 상태</summary>
    public enum BattleState
    {
        None,
        Loading,
        Running,
        Ending
    }
    
    /// <summary>Desync 이벤트 인자</summary>
    public class DesyncEventArgs : EventArgs
    {
        public int Tick { get; set; }
        public uint LocalHash { get; set; }
        public uint ServerHash { get; set; }
    }
}
