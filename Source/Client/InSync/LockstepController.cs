using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using HybridShared.Packets;

namespace HybridClient.InSync
{
    /// <summary>
    /// InSync 상태
    /// </summary>
    public enum InSyncState
    {
        None,           // 비동기 모드
        Requesting,     // 요청 대기 중
        Loading,        // 맵 로딩 중
        Active,         // Lockstep 활성화
        Ending          // 종료 중
    }
    
    /// <summary>
    /// InSync 역할
    /// </summary>
    public enum InSyncRole
    {
        None,
        Authority,      // 권위자 (정착지 소유자)
        Invader         // 침입자 (캐러밴)
    }
    
    /// <summary>
    /// Lockstep 동기화 컨트롤러
    /// MP AsyncTimeComp 참조 - Tick/PreContext/PostContext 패턴 적용
    /// </summary>
    public class LockstepController
    {
        private static LockstepController _instance;
        public static LockstepController Instance => _instance ??= new LockstepController();
        
        /// <summary>명령 지연 틱 수</summary>
        public const int COMMAND_DELAY = 2;
        
        // ========== 상태 ==========
        
        /// <summary>현재 InSync 상태</summary>
        public InSyncState State { get; private set; } = InSyncState.None;
        
        /// <summary>역할 (권위자/침입자)</summary>
        public InSyncRole Role { get; private set; } = InSyncRole.None;
        
        /// <summary>InSync 모드 (전투/협동)</summary>
        public InSyncMode Mode { get; private set; } = InSyncMode.Battle;
        
        /// <summary>세션 ID</summary>
        public int SessionId { get; private set; } = -1;
        
        /// <summary>상대 유저네임</summary>
        public string PartnerUsername { get; private set; }
        
        /// <summary>동기화 맵</summary>
        public Map SyncMap { get; private set; }
        
        /// <summary>현재 틱 (MP mapTicks 참조)</summary>
        public int MapTicks { get; private set; }
        
        /// <summary>현재 틱 (별칭)</summary>
        public int CurrentTick => MapTicks;
        
        /// <summary>난수 상태 (MP randState 참조)</summary>
        public ulong RandState { get; private set; } = 1;
        
        /// <summary>상대방 확인된 틱</summary>
        public int PartnerConfirmedTick { get; private set; }
        
        /// <summary>명령 큐 (MP cmds 참조)</summary>
        public Queue<LockstepCommandPacket> Cmds { get; private set; } = new();
        
        /// <summary>InSync 활성화 여부</summary>
        public bool IsActive => State == InSyncState.Active;
        
        // ========== 컨텍스트 저장용 ==========
        
        private int prevTicksGameInt;
        private ulong prevRandState;
        
        // ========== 진입/종료 ==========
        
        /// <summary>
        /// Lockstep 모드 진입 (권위자)
        /// </summary>
        public void EnterAsAuthority(int sessionId, string invaderUsername, Map map)
        {
            Log.Message($"[HybridMP][LOCKSTEP] Entering as Authority - Session {sessionId}, Invader: {invaderUsername}");
            
            SessionId = sessionId;
            PartnerUsername = invaderUsername;
            Role = InSyncRole.Authority;
            SyncMap = map;
            MapTicks = Find.TickManager.TicksGame;
            RandState = (ulong)(Rand.Int & 0xFFFFFFFF) | ((ulong)(Rand.Int & 0xFFFFFFFF) << 32);
            PartnerConfirmedTick = MapTicks;
            Cmds.Clear();
            State = InSyncState.Active;
        }
        
        /// <summary>
        /// Lockstep 모드 진입 (침입자)
        /// </summary>
        public void EnterAsInvader(int sessionId, string authorityUsername, Map map, int startTick, ulong randState)
        {
            Log.Message($"[HybridMP][LOCKSTEP] Entering as Invader - Session {sessionId}, Authority: {authorityUsername}");
            
            SessionId = sessionId;
            PartnerUsername = authorityUsername;
            Role = InSyncRole.Invader;
            SyncMap = map;
            MapTicks = startTick;
            RandState = randState;
            PartnerConfirmedTick = startTick;
            Cmds.Clear();
            State = InSyncState.Active;
        }
        
        /// <summary>
        /// Lockstep 종료
        /// </summary>
        public void ExitLockstep(string reason = null)
        {
            Log.Message($"[HybridMP][LOCKSTEP] Exiting Lockstep - Reason: {reason ?? "normal"}");
            
            State = InSyncState.None;
            Role = InSyncRole.None;
            SessionId = -1;
            PartnerUsername = null;
            SyncMap = null;
            Cmds.Clear();
        }
        
        // ========== 틱 실행 (MP AsyncTimeComp.Tick 참조) ==========
        
        /// <summary>
        /// Lockstep 틱 실행
        /// MP AsyncTimeComp.Tick() 패턴 적용
        /// </summary>
        public void Tick()
        {
            if (State != InSyncState.Active || SyncMap == null)
                return;
            
            // 맵 컴포넌트가 초기화되었는지 확인
            if (SyncMap.listerThings == null || SyncMap.mapPawns == null)
            {
                Log.Warning("[HybridMP][LOCKSTEP] Map components not initialized yet, skipping tick");
                return;
            }
            
            // 상대방 틱 확인 (Lockstep 동기화)
            // 상대방이 현재 틱까지 진행하지 않았으면 대기
            // TODO: 실제 구현에서는 서버를 통해 틱 확인 필요
            
            PreContext();
            
            try
            {
                // 현재 틱의 명령 실행 (CommandQueue.Instance 사용)
                CommandQueue.Instance.ExecuteForTick(MapTicks);
                
                // 맵 틱 진행 (null check 강화)
                if (SyncMap?.info != null)
                {
                    SyncMap.MapPreTick();
                    MapTicks++;
                    
                    // 맵 틱 진행 완료
                    SyncMap.MapPostTick();
                }
                
                // RimWorld 내부 틱과 동기화
                // TODO: Harmony Transpiler로 ticksGameInt/ticksThisFrame 접근 필요
                
                // 틱 동기화 패킷 전송
                SendTickConfirmation();
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][LOCKSTEP] Tick error: {e}");
            }
            finally
            {
                PostContext();
            }
        }
        
        /// <summary>
        /// 컨텍스트 설정 (MP PreContext 참조)
        /// </summary>
        private void PreContext()
        {
            // 이전 상태 저장
            prevTicksGameInt = Find.TickManager.TicksGame;
            prevRandState = (ulong)(Rand.Int & 0xFFFFFFFF);
            
            // 난수 상태 설정
            Rand.PushState();
            // RandState를 int로 변환하여 설정
            Rand.Seed = (int)(RandState & 0xFFFFFFFF);
        }
        
        /// <summary>
        /// 컨텍스트 복원 (MP PostContext 참조)
        /// </summary>
        private void PostContext()
        {
            // 난수 상태 저장
            RandState = (ulong)(Rand.Int & 0xFFFFFFFF) | ((ulong)(Rand.Int & 0xFFFFFFFF) << 32);
            Rand.PopState();
        }
        
        /// <summary>
        /// 명령 실행 (MP ExecuteCmd 참조)
        /// </summary>
        public void ExecuteCommands()
        {
            while (Cmds.Count > 0)
            {
                var cmd = Cmds.Peek();
                if (cmd.ExecuteTick > MapTicks)
                    break;
                
                Cmds.Dequeue();
                ExecuteCommand(cmd);
            }
        }
        
        /// <summary>
        /// 단일 명령 실행
        /// </summary>
        public void ExecuteCommand(LockstepCommandPacket cmd)
        {
            Log.Message($"[HybridMP][LOCKSTEP] Executing command type {cmd.CommandType} at tick {cmd.ExecuteTick}");
            
            byte[] data = cmd.GetCommandData();
            
            switch (cmd.CommandType)
            {
                case 0: // Sync
                    // 일반 동기화 명령
                    break;
                    
                case 1: // Draft
                    ExecuteDraftCommand(data);
                    break;
                    
                case 2: // Move
                    ExecuteMoveCommand(data);
                    break;
                    
                case 3: // TimeSpeed
                    ExecuteTimeSpeedCommand(data);
                    break;
                    
                default:
                    Log.Warning($"[HybridMP][LOCKSTEP] Unknown command type: {cmd.CommandType}");
                    break;
            }
        }
        
        private void ExecuteDraftCommand(byte[] data)
        {
            if (data == null || data.Length < 5)
                return;
            
            bool drafted = data[0] == 1;
            int pawnId = data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24);
            
            var thing = SyncMap?.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == pawnId);
            if (thing is Pawn pawn && pawn.drafter != null)
            {
                pawn.drafter.Drafted = drafted;
                Log.Message($"[HybridMP][LOCKSTEP] Draft executed: Pawn {pawnId} = {drafted}");
            }
        }
        
        private void ExecuteMoveCommand(byte[] data)
        {
            // TODO: Move 명령 구현
        }
        
        private void ExecuteTimeSpeedCommand(byte[] data)
        {
            if (data == null || data.Length < 1)
                return;
            
            int speed = data[0];
            if (speed >= 0 && speed <= 4)
            {
                Find.TickManager.CurTimeSpeed = (TimeSpeed)speed;
                Log.Message($"[HybridMP][LOCKSTEP] TimeSpeed set to {speed}");
            }
        }
        
        /// <summary>
        /// 틱 확인 패킷 전송
        /// </summary>
        private void SendTickConfirmation()
        {
            var packet = new LockstepTickPacket
            {
                SessionId = SessionId,
                Tick = MapTicks
            };
            NetworkManager.Instance?.Send(packet);
        }
        
        /// <summary>
        /// 명령 추가
        /// </summary>
        public void EnqueueCommand(LockstepCommandPacket cmd)
        {
            Cmds.Enqueue(cmd);
        }
        
        /// <summary>
        /// 상대방 틱 업데이트
        /// </summary>
        public void UpdatePartnerTick(int tick)
        {
            PartnerConfirmedTick = tick;
        }
        
        // ========== 상태 설정 ==========
        
        public void SetRequesting() => State = InSyncState.Requesting;
        public void SetLoading() => State = InSyncState.Loading;
        
        // ========== 상태 확인 ==========
        
        public bool IsInLockstep => State == InSyncState.Active;
        public bool IsAuthority => Role == InSyncRole.Authority;
        public bool IsInvader => Role == InSyncRole.Invader;
    }
}
