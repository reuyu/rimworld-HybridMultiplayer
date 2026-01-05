using System;
using System.Collections.Generic;
using System.Linq;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Battle
{
    /// <summary>
    /// 전투 매니저 - 모든 전투 세션을 관리.
    /// 세션 생성/조회/종료, 틱 업데이트, Desync 처리.
    /// </summary>
    public class BattleManager
    {
        /// <summary>활성 전투 세션들</summary>
        private Dictionary<string, BattleSession> sessions = new();
        
        /// <summary>틱 업데이트 간격 (프레임)</summary>
        private int tickInterval = 3;  // 3프레임마다 1틱 (약 20Hz at 60fps)
        private int tickCounter = 0;
        
        /// <summary>난수 생성기</summary>
        private Random random = new();
        
        /// <summary>동기화 패킷 브로드캐스트 이벤트</summary>
        public event Action<string, BattleSyncPacket> OnBroadcastSync;
        
        /// <summary>Fast Resync 패킷 전송 이벤트</summary>
        public event Action<string, int, AuthoritativeStatePacket> OnSendResync;
        
        /// <summary>전투 시작 알림 이벤트</summary>
        public event Action<BattleSession, BattleStartPacket> OnBattleStart;
        
        /// <summary>전투 종료 알림 이벤트</summary>
        public event Action<string, BattleEndPacket> OnBattleEnd;
        
        /// <summary>활성 전투 수</summary>
        public int ActiveBattleCount => sessions.Count;
        
        public BattleManager()
        {
            HybridLogger.Log(LogCategory.Battle, "BattleManager initialized");
        }
        
        /// <summary>전투 세션 생성</summary>
        public BattleSession CreateBattle(int[] participantIds)
        {
            if (participantIds == null || participantIds.Length < 2)
            {
                HybridLogger.Warn(LogCategory.Battle, 
                    "Cannot create battle: need at least 2 participants");
                return null;
            }
            
            // 고유 ID 생성 (8자리 hex)
            string battleId = Guid.NewGuid().ToString("N").Substring(0, 8);
            int seed = random.Next();
            
            var session = new BattleSession(battleId, participantIds, seed);
            sessions[battleId] = session;
            
            HybridLogger.Log(LogCategory.Battle, 
                $"Battle created", 
                $"BattleId: {battleId}, Players: [{string.Join(", ", participantIds)}], Seed: {seed}");
            
            // 시작 패킷 생성 및 이벤트 발생
            var startPacket = new BattleStartPacket
            {
                BattleId = battleId,
                ParticipantIds = participantIds,
                RandomSeed = seed,
                StartTick = 0,
                MapData = null  // TODO: 맵 데이터 추가
            };
            
            OnBattleStart?.Invoke(session, startPacket);
            
            return session;
        }
        
        /// <summary>전투 세션 조회</summary>
        public BattleSession GetBattle(string battleId)
        {
            return sessions.GetValueOrDefault(battleId);
        }
        
        /// <summary>특정 클라이언트가 참여 중인 전투 조회</summary>
        public BattleSession GetBattleByClient(int clientId)
        {
            return sessions.Values.FirstOrDefault(s => 
                s.ParticipantClientIds.Contains(clientId) && 
                s.State != BattleState.Finished);
        }
        
        /// <summary>활성(Running) 상태인 첫 번째 전투 반환</summary>
        public BattleSession GetActiveBattle()
        {
            return sessions.Values.FirstOrDefault(s => s.State == BattleState.Running);
        }
        
        /// <summary>매 프레임 호출 - 모든 활성 전투 업데이트</summary>
        public void Update()
        {
            tickCounter++;
            if (tickCounter < tickInterval) return;
            tickCounter = 0;
            
            // 완료된 세션 정리용 리스트
            var toRemove = new List<string>();
            
            foreach (var (battleId, session) in sessions)
            {
                if (session.State == BattleState.Finished)
                {
                    toRemove.Add(battleId);
                    continue;
                }
                
                if (session.State != BattleState.Running)
                    continue;
                
                // 틱 처리 및 동기화 패킷 브로드캐스트
                var syncPacket = session.ProcessTick();
                if (syncPacket != null)
                {
                    OnBroadcastSync?.Invoke(battleId, syncPacket);
                }
            }
            
            // 완료된 세션 정리
            foreach (var battleId in toRemove)
            {
                sessions.Remove(battleId);
                HybridLogger.Verbose(LogCategory.Battle, 
                    $"Session removed from active list", 
                    $"BattleId: {battleId}");
            }
        }
        
        /// <summary>클라이언트 준비 완료 처리</summary>
        public void HandleClientReady(string battleId, int clientId)
        {
            if (!sessions.TryGetValue(battleId, out var session))
            {
                HybridLogger.Warn(LogCategory.Battle, 
                    $"Ready for unknown battle", 
                    $"BattleId: {battleId}, ClientId: {clientId}");
                return;
            }
            
            session.SetClientReady(clientId);
        }
        
        /// <summary>액션 수신 처리</summary>
        public void HandleAction(string battleId, ScheduledAction action)
        {
            if (!sessions.TryGetValue(battleId, out var session))
            {
                HybridLogger.Warn(LogCategory.Action, 
                    $"Action for unknown battle", 
                    $"BattleId: {battleId}, Action: {action}");
                return;
            }
            
            session.EnqueueAction(action);
        }
        
        /// <summary>클라이언트 해시 보고 처리</summary>
        public void HandleClientHash(string battleId, int clientId, int tick, uint hash)
        {
            if (!sessions.TryGetValue(battleId, out var session))
            {
                HybridLogger.Warn(LogCategory.Desync, 
                    $"Hash for unknown battle", 
                    $"BattleId: {battleId}");
                return;
            }
            
            var desyncInfo = session.CheckClientHash(clientId, tick, hash);
            if (desyncInfo.HasValue)
            {
                // Desync 감지됨 → Fast Resync 트리거
                TriggerFastResync(battleId, desyncInfo.Value);
            }
        }
        
        /// <summary>Fast Resync 트리거</summary>
        private void TriggerFastResync(string battleId, DesyncInfo info)
        {
            HybridLogger.Warn(LogCategory.Resync, 
                $"Triggering Fast Resync", 
                $"BattleId: {battleId}, {info}");
            
            // TODO: 서버의 권위 있는 상태를 생성하여 클라이언트에 전송
            // 현재는 로그만 출력
            
            var resyncPacket = new AuthoritativeStatePacket
            {
                ServerTick = info.Tick,
                Corrections = new List<ThingDeltaData>(),
                OrphanedThingIDs = new List<int>(),
                MissingThings = new List<ThingSnapshot>()
            };
            
            // Desync된 클라이언트에 전송
            OnSendResync?.Invoke(battleId, info.ClientId1, resyncPacket);
        }
        
        /// <summary>전투 종료</summary>
        public void EndBattle(string battleId, BattleResult result, int? winnerId = null)
        {
            if (!sessions.TryGetValue(battleId, out var session))
            {
                HybridLogger.Warn(LogCategory.Battle, 
                    $"Cannot end: unknown battle", 
                    $"BattleId: {battleId}");
                return;
            }
            
            session.End(result);
            
            // 종료 패킷 생성
            var endPacket = new BattleEndPacket
            {
                BattleId = battleId,
                Result = result,
                WinnerId = winnerId,
                DurationTicks = session.CurrentTick
            };
            
            OnBattleEnd?.Invoke(battleId, endPacket);
            
            // 세션은 다음 Update에서 정리됨
        }
        
        /// <summary>콘솔용 상태 출력</summary>
        public void PrintStatus()
        {
            Console.WriteLine($"[BattleManager] Active battles: {sessions.Count}");
            foreach (var (id, session) in sessions)
            {
                Console.WriteLine($"  - {id}: {session.State}, Tick: {session.CurrentTick}, Players: [{string.Join(", ", session.ParticipantClientIds)}]");
            }
        }
        
        /// <summary>디버그: 강제로 전투 시작 (클라이언트 Ready 없이)</summary>
        public void ForceStart(string battleId)
        {
            BattleSession session;
            
            if (string.IsNullOrEmpty(battleId))
            {
                // battleId가 없으면 가장 최근(Preparing 상태) 전투 찾기
                session = sessions.Values.FirstOrDefault(s => s.State == BattleState.Preparing);
                if (session == null)
                {
                    Console.WriteLine("[BATTLE] No preparing battle found to force start");
                    return;
                }
            }
            else
            {
                if (!sessions.TryGetValue(battleId, out session))
                {
                    Console.WriteLine($"[BATTLE] Unknown battle: {battleId}");
                    return;
                }
            }
            
            Console.WriteLine($"[BATTLE] Force starting: {session.BattleId}");
            session.ForceStart();
        }
    }
}
