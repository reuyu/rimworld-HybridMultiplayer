using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using HybridShared.Packets;

namespace HybridClient.InSync
{
    /// <summary>
    /// 명령 동기화 핸들러
    /// MP Syncing/Sync.cs 패턴 적용 - 명령 캡처 및 동기화
    /// </summary>
    public class SyncHandler
    {
        private static SyncHandler _instance;
        public static SyncHandler Instance => _instance ??= new SyncHandler();
        
        /// <summary>현재 명령 캡처 활성화 여부</summary>
        public bool IsCapturing { get; private set; }
        
        /// <summary>명령 지연 틱 수</summary>
        public const int COMMAND_DELAY = 2;
        
        /// <summary>
        /// 명령 캡처 시작
        /// </summary>
        public void StartCapturing()
        {
            IsCapturing = true;
            Log.Message("[HybridMP][SYNC] Command capturing started");
        }
        
        /// <summary>
        /// 명령 캡처 중지
        /// </summary>
        public void StopCapturing()
        {
            IsCapturing = false;
            Log.Message("[HybridMP][SYNC] Command capturing stopped");
        }
        
        /// <summary>
        /// 드래프트 명령 동기화
        /// </summary>
        public void SyncDraft(Pawn pawn, bool drafted)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            if (!InSyncFactionManager.IsMyPawn(pawn))
            {
                return;
            }
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Draft,
                TargetThingId = pawn.thingIDNumber,
                BoolValue = drafted
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Draft command sent: {pawn.Name} -> {drafted}");
        }
        
        /// <summary>
        /// 이동 명령 동기화
        /// </summary>
        public void SyncMove(Pawn pawn, IntVec3 target)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            if (!InSyncFactionManager.IsMyPawn(pawn))
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Move,
                TargetThingId = pawn.thingIDNumber,
                TargetX = target.x,
                TargetZ = target.z
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Move command: {pawn.Name} -> ({target.x}, {target.z})");
        }
        
        /// <summary>
        /// 공격 명령 동기화
        /// </summary>
        public void SyncAttack(Pawn pawn, LocalTargetInfo target)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            if (!InSyncFactionManager.IsMyPawn(pawn))
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Attack,
                TargetThingId = pawn.thingIDNumber,
                SecondaryThingId = target.Thing?.thingIDNumber ?? -1,
                TargetX = target.Cell.x,
                TargetZ = target.Cell.z
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Attack command: {pawn.Name} -> {target}");
        }
        
        /// <summary>
        /// 정지 명령 동기화
        /// </summary>
        public void SyncStop(Pawn pawn)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            if (!InSyncFactionManager.IsMyPawn(pawn))
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Stop,
                TargetThingId = pawn.thingIDNumber
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Stop command: {pawn.Name}");
        }
        
        // ========== MP 스타일 일반화된 동기화 ==========
        
        /// <summary>
        /// Gizmo(버튼) 클릭 동기화 (MP Command 패턴)
        /// </summary>
        public void SyncGizmoPress(Command gizmo)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Gizmo,
                StringValue = gizmo.GetType().FullName,
                // Gizmo의 고유 식별자 (가능하면)
                TargetThingId = gizmo.GetHashCode()
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Gizmo press: {gizmo.GetType().Name}");
        }
        
        /// <summary>
        /// Designator 셀 지정 동기화 (MP Designator 패턴)
        /// </summary>
        public void SyncDesignation(Designator designator, IntVec3 cell)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Designate,
                StringValue = designator.GetType().FullName,
                TargetX = cell.x,
                TargetZ = cell.z
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Designate cell: {designator.GetType().Name} at ({cell.x}, {cell.z})");
        }
        
        /// <summary>
        /// Designator Thing 지정 동기화
        /// </summary>
        public void SyncDesignation(Designator designator, Thing thing)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.Designate,
                StringValue = designator.GetType().FullName,
                TargetThingId = thing.thingIDNumber
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] Designate thing: {designator.GetType().Name} on {thing.Label}");
        }
        
        /// <summary>
        /// FloatMenu 선택 동기화 (MP FloatMenu 패턴)
        /// </summary>
        public void SyncFloatMenuChoice(FloatMenuOption option)
        {
            if (!IsCapturing || !InSyncManager.Instance.IsActive)
                return;
            
            var cmd = new InSyncCommand
            {
                Type = InSyncCommandType.FloatMenu,
                StringValue = option.Label
            };
            
            SendCommand(cmd);
            Log.Message($"[HybridMP][SYNC] FloatMenu choice: {option.Label}");
        }
        
        /// <summary>
        /// 명령을 서버로 전송
        /// </summary>
        private void SendCommand(InSyncCommand cmd)
        {
            int executeTick = LockstepController.Instance.MapTicks + COMMAND_DELAY;
            
            var packet = new LockstepCommandPacket
            {
                SessionId = InSyncManager.Instance.CurrentSessionId,
                ExecuteTick = executeTick,
                CommandType = (byte)cmd.Type,
                CommandDataBase64 = SerializeCommand(cmd)
            };
            
            // 로컬 큐에 추가 (자신의 명령 즉시 실행용)
            CommandQueue.Instance.Enqueue(packet);
            
            // 네트워크 전송 (상대방에게 명령 전달)
            NetworkManager.Instance?.Send(packet);
            Log.Message($"[HybridMP][SYNC] Command sent for tick {executeTick}");
        }
        
        /// <summary>
        /// 수신된 명령 실행
        /// </summary>
        public void ExecuteCommand(LockstepCommandPacket packet)
        {
            try
            {
                var cmd = DeserializeCommand(packet.CommandDataBase64);
                if (cmd == null) return;
                
                switch (cmd.Type)
                {
                    case InSyncCommandType.Draft:
                        ExecuteDraft(cmd);
                        break;
                    case InSyncCommandType.Move:
                        ExecuteMove(cmd);
                        break;
                    case InSyncCommandType.Attack:
                        ExecuteAttack(cmd);
                        break;
                    case InSyncCommandType.Stop:
                        ExecuteStop(cmd);
                        break;
                    default:
                        Log.Warning($"[HybridMP][SYNC] Unknown command type: {cmd.Type}");
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][SYNC] Failed to execute command: {e}");
            }
        }
        
        private void ExecuteDraft(InSyncCommand cmd)
        {
            var pawn = FindThing(cmd.TargetThingId) as Pawn;
            if (pawn?.drafter == null) return;
            
            pawn.drafter.Drafted = cmd.BoolValue;
            Log.Message($"[HybridMP][SYNC] Executed draft: {pawn.Name} -> {cmd.BoolValue}");
        }
        
        private void ExecuteMove(InSyncCommand cmd)
        {
            var pawn = FindThing(cmd.TargetThingId) as Pawn;
            if (pawn == null) return;
            
            var target = new IntVec3(cmd.TargetX, 0, cmd.TargetZ);
            var map = InSyncManager.Instance.SyncMap;
            
            if (map != null && target.InBounds(map))
            {
                // 이동 작업 생성
                var job = new Verse.AI.Job(RimWorld.JobDefOf.Goto, target);
                pawn.jobs?.StartJob(job, Verse.AI.JobCondition.InterruptForced, null, false, true, null, null, false);
                Log.Message($"[HybridMP][SYNC] Executed move: {pawn.Name} -> ({target.x}, {target.z})");
            }
        }
        
        private void ExecuteAttack(InSyncCommand cmd)
        {
            var pawn = FindThing(cmd.TargetThingId) as Pawn;
            if (pawn == null) return;
            
            LocalTargetInfo target;
            if (cmd.SecondaryThingId >= 0)
            {
                var targetThing = FindThing(cmd.SecondaryThingId);
                target = targetThing != null ? new LocalTargetInfo(targetThing) : LocalTargetInfo.Invalid;
            }
            else
            {
                target = new LocalTargetInfo(new IntVec3(cmd.TargetX, 0, cmd.TargetZ));
            }
            
            if (target.IsValid)
            {
                // 공격 작업 생성
                var job = new Verse.AI.Job(RimWorld.JobDefOf.AttackMelee, target);
                pawn.jobs?.StartJob(job, Verse.AI.JobCondition.InterruptForced, null, false, true, null, null, false);
                Log.Message($"[HybridMP][SYNC] Executed attack: {pawn.Name} -> {target}");
            }
        }
        
        private void ExecuteStop(InSyncCommand cmd)
        {
            var pawn = FindThing(cmd.TargetThingId) as Pawn;
            if (pawn == null) return;
            
            pawn.jobs?.StopAll();
            Log.Message($"[HybridMP][SYNC] Executed stop: {pawn.Name}");
        }
        
        private Thing FindThing(int thingId)
        {
            var map = InSyncManager.Instance.SyncMap;
            if (map == null) return null;
            
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.thingIDNumber == thingId)
                    return thing;
            }
            return null;
        }
        
        private string SerializeCommand(InSyncCommand cmd)
        {
            // ===== 바이너리 직렬화 (MP ByteWriter 패턴) =====
            var writer = new HybridShared.ByteWriter();
            
            writer.WriteByte((byte)cmd.Type);
            writer.WriteInt(cmd.TargetThingId);
            writer.WriteInt(cmd.SecondaryThingId);
            writer.WriteInt(cmd.TargetX);
            writer.WriteInt(cmd.TargetZ);
            writer.WriteBool(cmd.BoolValue);
            writer.WriteString(cmd.StringValue);
            
            return Convert.ToBase64String(writer.ToArray());
        }
        
        private InSyncCommand DeserializeCommand(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            
            try 
            {
                // ===== 바이너리 역직렬화 (MP ByteReader 패턴) =====
                byte[] bytes = Convert.FromBase64String(data);
                var reader = new HybridShared.ByteReader(bytes);
                
                return new InSyncCommand
                {
                    Type = (InSyncCommandType)reader.ReadByte(),
                    TargetThingId = reader.ReadInt(),
                    SecondaryThingId = reader.ReadInt(),
                    TargetX = reader.ReadInt(),
                    TargetZ = reader.ReadInt(),
                    BoolValue = reader.ReadBool(),
                    StringValue = reader.ReadString()
                };
            }
            catch
            {
                return null;
            }
        }
    }
    
    /// <summary>
    /// InSync 명령 타입
    /// </summary>
    public enum InSyncCommandType
    {
        None = 0,       // 예약 (Lockstep Sync용)
        Draft = 1,
        Move = 2,
        Attack = 3,
        Stop = 4,
        Custom = 5,
        
        // ===== MP 스타일 일반화 타입 =====
        Gizmo = 10,     // UI 버튼 클릭
        Designate = 11, // 지정 (건설, 채굴 등)
        FloatMenu = 12, // 우클릭 메뉴
        SyncMethod = 13 // 일반 메서드 호출
    }
    
    /// <summary>
    /// InSync 명령 데이터
    /// </summary>
    public class InSyncCommand
    {
        public InSyncCommandType Type;
        public int TargetThingId;
        public int SecondaryThingId;
        public int TargetX;
        public int TargetZ;
        public bool BoolValue;
        public string StringValue;
    }
}

