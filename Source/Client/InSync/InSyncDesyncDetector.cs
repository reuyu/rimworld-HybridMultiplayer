using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using HybridShared.Packets;

namespace HybridClient.InSync
{
    /// <summary>
    /// InSync 중 Desync 감지
    /// MP SyncCoordinator/ClientSyncOpinion 패턴 적용
    /// </summary>
    public class InSyncDesyncDetector
    {
        private static InSyncDesyncDetector _instance;
        public static InSyncDesyncDetector Instance => _instance ??= new InSyncDesyncDetector();
        
        /// <summary>Desync 발생 여부</summary>
        public bool IsDesynced { get; private set; }
        
        /// <summary>마지막 유효 틱</summary>
        public int LastValidTick { get; private set; } = -1;
        
        // 현재 수집 중인 opinion
        private SyncOpinion currentOpinion;
        
        // 상대방에게서 받은 opinions
        private Queue<SyncOpinion> partnerOpinions = new Queue<SyncOpinion>();
        
        /// <summary>
        /// InSync 시작 시 초기화
        /// </summary>
        public void Initialize(int startTick)
        {
            IsDesynced = false;
            LastValidTick = startTick;
            currentOpinion = new SyncOpinion(startTick);
            partnerOpinions.Clear();
            
            Log.Message($"[HybridMP][DESYNC] Detector initialized at tick {startTick}");
        }
        
        /// <summary>
        /// 틱 완료 시 랜덤 상태 수집
        /// MP SyncCoordinator.TryAddMapRandomState 패턴
        /// </summary>
        public void RecordTickState(int tick, uint randStateHash)
        {
            if (IsDesynced) return;
            
            currentOpinion?.AddTickState(tick, randStateHash);
        }
        
        /// <summary>
        /// 명령 실행 후 랜덤 상태 수집
        /// </summary>
        public void RecordCommandState(uint randStateHash)
        {
            if (IsDesynced) return;
            
            currentOpinion?.AddCommandState(randStateHash);
        }
        
        /// <summary>
        /// 주기적으로 opinion 전송 (예: 30틱마다)
        /// </summary>
        public void CheckAndSendOpinion(int currentTick)
        {
            if (IsDesynced || currentOpinion == null) return;
            
            // 30틱마다 opinion 전송
            if (currentTick > 0 && currentTick % 30 == 0)
            {
                SendOpinion(currentOpinion);
                
                // 새 opinion 시작
                currentOpinion = new SyncOpinion(currentTick);
            }
        }
        
        /// <summary>
        /// 상대방 opinion 수신
        /// </summary>
        public void ReceivePartnerOpinion(SyncOpinionPacket packet)
        {
            if (IsDesynced) return;
            
            var partnerOpinion = SyncOpinion.FromPacket(packet);
            partnerOpinions.Enqueue(partnerOpinion);
            
            // 비교 가능한 opinion이 있으면 비교
            TryCompareOpinions();
        }
        
        /// <summary>
        /// Opinion 비교
        /// </summary>
        private void TryCompareOpinions()
        {
            if (currentOpinion == null || partnerOpinions.Count == 0) return;
            
            var partner = partnerOpinions.Peek();
            
            // 같은 틱 범위면 비교
            if (partner.StartTick == currentOpinion.StartTick)
            {
                partnerOpinions.Dequeue();
                
                string desyncMessage = currentOpinion.CheckForDesync(partner);
                
                if (desyncMessage != null)
                {
                    HandleDesync(desyncMessage, currentOpinion.StartTick);
                }
                else
                {
                    LastValidTick = currentOpinion.StartTick;
                    Log.Message($"[HybridMP][DESYNC] Sync verified at tick {LastValidTick}");
                }
            }
        }
        
        /// <summary>
        /// Desync 처리
        /// </summary>
        private void HandleDesync(string message, int tick)
        {
            IsDesynced = true;
            
            Log.Error($"[HybridMP][DESYNC] DESYNC DETECTED at tick {tick}: {message}");
            
            // 사용자에게 알림
            Messages.Message($"Desync detected: {message}", MessageTypeDefOf.NegativeEvent, true);
            
            // InSync 종료 요청
            InSyncManager.Instance.RequestEnd($"Desync: {message}");
        }
        
        /// <summary>
        /// Opinion 패킷 전송
        /// </summary>
        private void SendOpinion(SyncOpinion opinion)
        {
            var packet = opinion.ToPacket();
            packet.SessionId = InSyncManager.Instance.CurrentSessionId;
            
            NetworkManager.Instance?.Send(packet);
            
            Log.Message($"[HybridMP][DESYNC] Sent opinion for tick {opinion.StartTick}");
        }
        
        /// <summary>
        /// 정리
        /// </summary>
        public void Cleanup()
        {
            IsDesynced = false;
            LastValidTick = -1;
            currentOpinion = null;
            partnerOpinions.Clear();
            
            Log.Message("[HybridMP][DESYNC] Detector cleaned up");
        }
    }
    
    /// <summary>
    /// 동기화 상태 정보 (MP ClientSyncOpinion 패턴)
    /// </summary>
    public class SyncOpinion
    {
        public int StartTick { get; private set; }
        
        // 틱별 랜덤 상태 해시
        private List<uint> tickStates = new List<uint>();
        
        // 명령별 랜덤 상태 해시
        private List<uint> commandStates = new List<uint>();
        
        public SyncOpinion(int startTick)
        {
            StartTick = startTick;
        }
        
        public void AddTickState(int tick, uint stateHash)
        {
            tickStates.Add(stateHash);
        }
        
        public void AddCommandState(uint stateHash)
        {
            commandStates.Add(stateHash);
        }
        
        /// <summary>
        /// Desync 체크
        /// </summary>
        public string CheckForDesync(SyncOpinion other)
        {
            // 틱 상태 비교
            if (tickStates.Count != other.tickStates.Count)
                return $"Tick count mismatch: {tickStates.Count} vs {other.tickStates.Count}";
            
            for (int i = 0; i < tickStates.Count; i++)
            {
                if (tickStates[i] != other.tickStates[i])
                    return $"Tick state differs at index {i}: {tickStates[i]:X8} vs {other.tickStates[i]:X8}";
            }
            
            // 명령 상태 비교
            if (commandStates.Count != other.commandStates.Count)
                return $"Command count mismatch: {commandStates.Count} vs {other.commandStates.Count}";
            
            for (int i = 0; i < commandStates.Count; i++)
            {
                if (commandStates[i] != other.commandStates[i])
                    return $"Command state differs at index {i}: {commandStates[i]:X8} vs {other.commandStates[i]:X8}";
            }
            
            return null; // 동기화됨
        }
        
        /// <summary>
        /// 패킷으로 변환
        /// </summary>
        public SyncOpinionPacket ToPacket()
        {
            return new SyncOpinionPacket
            {
                StartTick = StartTick,
                TickStates = tickStates,
                CommandStates = commandStates
            };
        }
        
        /// <summary>
        /// 패킷에서 생성
        /// </summary>
        public static SyncOpinion FromPacket(SyncOpinionPacket packet)
        {
            var opinion = new SyncOpinion(packet.StartTick);
            opinion.tickStates = new List<uint>(packet.TickStates ?? new List<uint>());
            opinion.commandStates = new List<uint>(packet.CommandStates ?? new List<uint>());
            return opinion;
        }
    }
}
