using System;
using System.Collections.Generic;
using System.Linq;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Battle
{
    /// <summary>
    /// 전투 세션 - 개별 전투의 상태와 로직을 관리.
    /// 서버가 마스터 틱을 관리하고, 클라이언트들의 상태 해시를 비교하여 Desync를 감지.
    /// </summary>
    public class BattleSession
    {
        /// <summary>고유 전투 ID</summary>
        public string BattleId { get; }
        
        /// <summary>현재 상태</summary>
        public BattleState State { get; private set; } = BattleState.Preparing;
        
        /// <summary>현재 서버 틱</summary>
        public int CurrentTick { get; private set; }
        
        /// <summary>난수 시드</summary>
        public int RandomSeed { get; }
        
        /// <summary>참가자 클라이언트 ID 목록</summary>
        public List<int> ParticipantClientIds { get; }
        
        /// <summary>준비 완료된 클라이언트</summary>
        private HashSet<int> readyClients = new();
        
        /// <summary>틱별 액션 큐</summary>
        private Dictionary<int, List<ScheduledAction>> actionsByTick = new();
        
        /// <summary>클라이언트별 상태 해시 (틱별)</summary>
        private Dictionary<int, Dictionary<int, uint>> clientStateHashes = new();
        
        /// <summary>서버 상태 해시 (틱별)</summary>
        private Dictionary<int, uint> serverStateHashes = new();
        
        /// <summary>생성 시간</summary>
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        
        /// <summary>시작 시간</summary>
        public DateTime? StartedAt { get; private set; }
        
        public BattleSession(string battleId, int[] participants, int seed)
        {
            BattleId = battleId;
            ParticipantClientIds = participants.ToList();
            RandomSeed = seed;
            CurrentTick = 0;
            
            HybridLogger.Log(LogCategory.Battle, 
                "Session created", 
                $"BattleId: {battleId}, Seed: {seed}, Players: [{string.Join(", ", participants)}]");
        }
        
        /// <summary>클라이언트 준비 완료 처리</summary>
        public bool SetClientReady(int clientId)
        {
            if (!ParticipantClientIds.Contains(clientId))
            {
                HybridLogger.Warn(LogCategory.Battle, 
                    $"Unknown client {clientId} tried to ready", 
                    $"BattleId: {BattleId}");
                return false;
            }
            
            readyClients.Add(clientId);
            HybridLogger.Log(LogCategory.Battle, 
                $"Client {clientId} ready ({readyClients.Count}/{ParticipantClientIds.Count})", 
                $"BattleId: {BattleId}");
            
            // 모든 클라이언트가 준비되면 시작
            if (readyClients.Count >= ParticipantClientIds.Count)
            {
                Start();
                return true;
            }
            return false;
        }
        
        /// <summary>전투 시작</summary>
        public void Start()
        {
            if (State != BattleState.Preparing)
            {
                HybridLogger.Warn(LogCategory.Battle, 
                    $"Cannot start: already in state {State}", 
                    $"BattleId: {BattleId}");
                return;
            }
            
            State = BattleState.Running;
            StartedAt = DateTime.UtcNow;
            
            HybridLogger.Log(LogCategory.Battle, 
                "Battle STARTED!", 
                $"BattleId: {BattleId}, Players: {ParticipantClientIds.Count}");
        }
        
        /// <summary>디버그: 강제 시작 (Ready 없이)</summary>
        public void ForceStart()
        {
            Console.WriteLine($"[BATTLE] Force starting battle {BattleId} (current state: {State})");
            
            State = BattleState.Running;
            StartedAt = DateTime.UtcNow;
            
            HybridLogger.Log(LogCategory.Battle, 
                "Battle FORCE STARTED!", 
                $"BattleId: {BattleId}, Players: {ParticipantClientIds.Count}");
        }
        
        /// <summary>액션 추가</summary>
        public void EnqueueAction(ScheduledAction action)
        {
            int tick = action.ExecuteTick;
            
            // 이미 지난 틱의 액션은 다음 틱으로
            if (tick <= CurrentTick)
            {
                HybridLogger.Warn(LogCategory.Action, 
                    $"Action for past tick {tick}, rescheduling to {CurrentTick + 1}", 
                    $"BattleId: {BattleId}");
                tick = CurrentTick + 1;
                action.ExecuteTick = tick;
            }
            
            if (!actionsByTick.ContainsKey(tick))
                actionsByTick[tick] = new List<ScheduledAction>();
            
            actionsByTick[tick].Add(action);
            
            HybridLogger.Verbose(LogCategory.Action, 
                $"Action queued: {action}", 
                $"BattleId: {BattleId}");
        }
        
        /// <summary>틱 처리 (서버 마스터 틱)</summary>
        public BattleSyncPacket ProcessTick()
        {
            if (State != BattleState.Running)
                return null;
            
            CurrentTick++;
            
            // 해당 틱의 액션들 가져오기
            var actions = actionsByTick.GetValueOrDefault(CurrentTick, new List<ScheduledAction>());
            
            // 처리된 액션 제거
            actionsByTick.Remove(CurrentTick);
            
            // 서버 상태 해시 계산 (현재는 단순히 틱 기반)
            uint serverHash = ComputeServerStateHash();
            serverStateHashes[CurrentTick] = serverHash;
            
            // 오래된 해시 정리 (최근 100틱만 유지)
            CleanupOldHashes();
            
            // 매 10틱마다 로그 출력 (디버그용)
            if (CurrentTick % 10 == 0)
            {
                HybridLogger.Log(LogCategory.Tick, 
                    $"Tick {CurrentTick} processed", 
                    $"BattleId: {BattleId}, Hash: 0x{serverHash:X8}");
            }
            
            return new BattleSyncPacket
            {
                BattleId = BattleId,
                ServerTick = CurrentTick,
                Actions = actions,
                ServerStateHash = serverHash
            };
        }
        
        /// <summary>클라이언트 해시 보고 처리</summary>
        public DesyncInfo? CheckClientHash(int clientId, int tick, uint hash)
        {
            // 해당 클라이언트의 해시 저장
            if (!clientStateHashes.ContainsKey(clientId))
                clientStateHashes[clientId] = new Dictionary<int, uint>();
            
            clientStateHashes[clientId][tick] = hash;
            
            HybridLogger.Verbose(LogCategory.Desync, 
                $"Client {clientId} hash for tick {tick}: 0x{hash:X8}", 
                $"BattleId: {BattleId}");
            
            // 서버 해시와 비교
            if (serverStateHashes.TryGetValue(tick, out uint serverHash))
            {
                if (hash != serverHash)
                {
                    HybridLogger.Warn(LogCategory.Desync, 
                        $"DESYNC DETECTED! Client {clientId} differs from server", 
                        $"Tick: {tick}, Client: 0x{hash:X8}, Server: 0x{serverHash:X8}");
                    
                    return new DesyncInfo
                    {
                        Tick = tick,
                        ClientId1 = clientId,
                        Hash1 = hash,
                        ClientId2 = -1, // 서버
                        Hash2 = serverHash
                    };
                }
            }
            
            // 다른 클라이언트와 비교
            foreach (var (otherId, hashes) in clientStateHashes)
            {
                if (otherId == clientId) continue;
                if (hashes.TryGetValue(tick, out uint otherHash) && hash != otherHash)
                {
                    HybridLogger.Warn(LogCategory.Desync, 
                        $"DESYNC DETECTED between clients!", 
                        $"Tick: {tick}, Client{clientId}: 0x{hash:X8}, Client{otherId}: 0x{otherHash:X8}");
                    
                    return new DesyncInfo
                    {
                        Tick = tick,
                        ClientId1 = clientId,
                        Hash1 = hash,
                        ClientId2 = otherId,
                        Hash2 = otherHash
                    };
                }
            }
            
            return null;
        }
        
        /// <summary>서버 상태 해시 계산</summary>
        private uint ComputeServerStateHash()
        {
            // TODO: 실제 게임 상태 기반 해시 계산
            // 현재는 틱 + 액션 수 기반의 간단한 해시
            uint hash = (uint)CurrentTick;
            
            foreach (var (tick, actions) in actionsByTick)
            {
                hash ^= (uint)(tick * 31);
                hash ^= (uint)(actions.Count * 17);
            }
            
            return hash;
        }
        
        /// <summary>오래된 해시 정리</summary>
        private void CleanupOldHashes()
        {
            int threshold = CurrentTick - 100;
            if (threshold <= 0) return;
            
            // 서버 해시 정리
            var oldServerTicks = serverStateHashes.Keys.Where(t => t < threshold).ToList();
            foreach (var tick in oldServerTicks)
                serverStateHashes.Remove(tick);
            
            // 클라이언트 해시 정리
            foreach (var clientHashes in clientStateHashes.Values)
            {
                var oldClientTicks = clientHashes.Keys.Where(t => t < threshold).ToList();
                foreach (var tick in oldClientTicks)
                    clientHashes.Remove(tick);
            }
        }
        
        /// <summary>전투 종료</summary>
        public void End(BattleResult result)
        {
            State = BattleState.Finished;
            
            var duration = DateTime.UtcNow - (StartedAt ?? CreatedAt);
            
            HybridLogger.Log(LogCategory.Battle, 
                $"Battle ENDED: {result}", 
                $"BattleId: {BattleId}, Duration: {duration.TotalSeconds:F1}s, Ticks: {CurrentTick}");
        }
    }
    
    /// <summary>Desync 정보</summary>
    public struct DesyncInfo
    {
        public int Tick;
        public int ClientId1;
        public uint Hash1;
        public int ClientId2;  // -1 = 서버
        public uint Hash2;
        
        public override string ToString()
        {
            string client2 = ClientId2 == -1 ? "Server" : $"Client{ClientId2}";
            return $"Desync@Tick{Tick}: Client{ClientId1}(0x{Hash1:X8}) vs {client2}(0x{Hash2:X8})";
        }
    }
    
    /// <summary>전투 상태</summary>
    public enum BattleState
    {
        /// <summary>참가자 대기 중</summary>
        Preparing,
        /// <summary>맵 로딩 중</summary>
        Loading,
        /// <summary>전투 진행 중</summary>
        Running,
        /// <summary>일시정지</summary>
        Paused,
        /// <summary>종료 처리 중</summary>
        Ending,
        /// <summary>완료</summary>
        Finished
    }
}
