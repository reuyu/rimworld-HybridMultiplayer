using System.Collections.Generic;
using Verse;
using HybridShared.Packets;

namespace HybridClient.InSync
{
    /// <summary>
    /// 명령 큐 - MP AsyncTimeComp.cmds 참조
    /// 명령을 특정 틱에서 실행하도록 큐잉
    /// </summary>
    public class CommandQueue
    {
        private static CommandQueue _instance;
        public static CommandQueue Instance => _instance ??= new CommandQueue();
        
        /// <summary>틱별 명령 큐</summary>
        private readonly Dictionary<int, List<LockstepCommandPacket>> _tickCommands = new();
        
        /// <summary>다음 실행할 틱</summary>
        public int NextExecuteTick { get; private set; }
        
        /// <summary>명령 실행 중 플래그 (재귀 방지)</summary>
        public bool IsExecutingCommand { get; private set; }
        
        /// <summary>
        /// 명령 추가
        /// </summary>
        public void Enqueue(LockstepCommandPacket command)
        {
            if (!_tickCommands.TryGetValue(command.ExecuteTick, out var commands))
            {
                commands = new List<LockstepCommandPacket>();
                _tickCommands[command.ExecuteTick] = commands;
            }
            
            commands.Add(command);
            Log.Message($"[HybridMP][CMDQUEUE] Enqueued command type {command.CommandType} for tick {command.ExecuteTick}");
        }
        
        /// <summary>
        /// 특정 틱의 명령 실행
        /// </summary>
        public void ExecuteForTick(int tick)
        {
            if (!_tickCommands.TryGetValue(tick, out var commands))
                return;
            
            IsExecutingCommand = true;
            try
            {
                foreach (var cmd in commands)
                {
                    ExecuteCommand(cmd);
                }
            }
            finally
            {
                IsExecutingCommand = false;
            }
            
            _tickCommands.Remove(tick);
            NextExecuteTick = tick + 1;
        }
        
        /// <summary>
        /// 명령 실행
        /// MP AsyncTimeComp.ExecuteCmd() 참조
        /// </summary>
        private void ExecuteCommand(LockstepCommandPacket cmd)
        {
            Log.Message($"[HybridMP][CMDQUEUE] Executing command type {cmd.CommandType} from {cmd.SenderUsername}");
            
            byte[] data = cmd.GetCommandData();
            
            switch (cmd.CommandType)
            {
                case 0: // Sync (일반 동기화)
                    // TODO: SyncHandler 구현
                    break;
                    
                case 1: // Draft 명령
                    ExecuteDraftCommand(data);
                    break;
                    
                case 2: // Move 명령
                    ExecuteMoveCommand(data);
                    break;
                    
                case 3: // TimeSpeed
                    ExecuteTimeSpeedCommand(data);
                    break;
                    
                default:
                    Log.Warning($"[HybridMP][CMDQUEUE] Unknown command type: {cmd.CommandType}");
                    break;
            }
        }
        
        /// <summary>
        /// Draft 명령 실행
        /// </summary>
        private void ExecuteDraftCommand(byte[] data)
        {
            if (data == null || data.Length < 5)
                return;
            
            bool drafted = data[0] == 1;
            int pawnId = data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24);
            
            // 폰 찾기
            foreach (var map in Find.Maps)
            {
                foreach (var thing in map.listerThings.AllThings)
                {
                    if (thing is Pawn pawn && pawn.thingIDNumber == pawnId)
                    {
                        if (pawn.drafter != null)
                        {
                            Log.Message($"[HybridMP][CMDQUEUE] Setting draft state for pawn {pawnId}: {drafted}");
                            pawn.drafter.Drafted = drafted;
                        }
                        return;
                    }
                }
            }
            
            Log.Warning($"[HybridMP][CMDQUEUE] Pawn {pawnId} not found for draft command");
        }
        
        /// <summary>
        /// Move 명령 실행
        /// </summary>
        private void ExecuteMoveCommand(byte[] data)
        {
            if (data == null || data.Length < 16)
                return;
            
            // TODO: Move 명령 구현
            // 폰 ID (4 bytes) + 목표 위치 (12 bytes: x, y, z)
        }
        
        /// <summary>
        /// TimeSpeed 명령 실행
        /// </summary>
        private void ExecuteTimeSpeedCommand(byte[] data)
        {
            if (data == null || data.Length < 1)
                return;
            
            int speed = data[0];
            Log.Message($"[HybridMP][CMDQUEUE] Setting time speed: {speed}");
            
            // TimeSpeed 설정
            if (speed >= 0 && speed <= 4)
            {
                Find.TickManager.CurTimeSpeed = (TimeSpeed)speed;
            }
        }
        
        /// <summary>
        /// 큐 초기화
        /// </summary>
        public void Clear()
        {
            _tickCommands.Clear();
            NextExecuteTick = 0;
        }
        
        /// <summary>
        /// 해당 틱의 명령이 있는지 확인
        /// </summary>
        public bool HasCommandsForTick(int tick)
        {
            return _tickCommands.ContainsKey(tick) && _tickCommands[tick].Count > 0;
        }
        
        /// <summary>
        /// 대기 중인 명령 수
        /// </summary>
        public int PendingCommandCount
        {
            get
            {
                int count = 0;
                foreach (var list in _tickCommands.Values)
                    count += list.Count;
                return count;
            }
        }
    }
}
