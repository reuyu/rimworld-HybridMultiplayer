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
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(cmd);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes);
        }
        
        private InSyncCommand DeserializeCommand(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            
            try 
            {
                byte[] bytes = Convert.FromBase64String(data);
                string json = System.Text.Encoding.UTF8.GetString(bytes);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<InSyncCommand>(json);
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
        None = 0,   // 예약 (Lockstep Sync용)
        Draft = 1,
        Move = 2,
        Attack = 3,
        Stop = 4,
        Custom = 5
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

