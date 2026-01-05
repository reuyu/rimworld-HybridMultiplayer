using HarmonyLib;
using RimWorld;
using Verse;
using HybridClient.InSync;

namespace HybridClient.Patches
{
    /// <summary>
    /// InSync 모드에서 틱 동기화를 위한 Harmony 패치
    /// MP AsyncTimeComp.Tick() 참조
    /// </summary>
    
    /// <summary>
    /// TickManager.DoSingleTick 패치 - Lockstep 모드에서 틱 제어
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    public static class TickManager_DoSingleTick_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(TickManager __instance)
        {
            // Lockstep 모드가 아니면 기본 동작
            if (!LockstepController.Instance.IsInLockstep)
                return true;
            
            // Lockstep 모드에서는 InSyncManager가 틱을 제어
            InSyncManager.Instance.Tick();
            
            // 원래 틱은 실행
            return true;
        }
    }
    
    /// <summary>
    /// 일시정지 제어 - 전투 모드에서 일시정지 불가
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TogglePaused))]
    public static class TickManager_TogglePaused_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // Lockstep 모드이고 전투 모드면 일시정지 불가
            if (LockstepController.Instance.IsInLockstep)
            {
                var mode = InSyncManager.Instance.Mode;
                if (mode == HybridShared.Packets.InSyncMode.Battle)
                {
                    Messages.Message("Cannot pause during battle!", MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// ESC 메뉴에서도 틱 계속 진행
    /// </summary>
    [HarmonyPatch(typeof(TickManager), "get_Paused")]
    public static class TickManager_Paused_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            // Lockstep 전투 모드에서는 절대 일시정지 안됨
            if (LockstepController.Instance.IsInLockstep &&
                InSyncManager.Instance.Mode == HybridShared.Packets.InSyncMode.Battle)
            {
                __result = false;
            }
        }
    }
    
    /// <summary>
    /// Draft 명령 동기화
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    public static class DraftController_Drafted_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_DraftController __instance, bool value)
        {
            if (!LockstepController.Instance.IsInLockstep)
                return true;
            
            // 이미 명령으로 실행 중이면 통과
            if (CommandQueue.Instance.IsExecutingCommand)
                return true;
            
            // Draft 명령을 네트워크로 전송
            var pawn = __instance.pawn;
            if (pawn != null)
            {
                byte[] data = new byte[5];
                data[0] = value ? (byte)1 : (byte)0;
                // pawn ID를 다음 4바이트에 저장
                int id = pawn.thingIDNumber;
                data[1] = (byte)(id & 0xFF);
                data[2] = (byte)((id >> 8) & 0xFF);
                data[3] = (byte)((id >> 16) & 0xFF);
                data[4] = (byte)((id >> 24) & 0xFF);
                
                InSyncManager.Instance.SendCommand(1, data); // 1 = Draft command
                
                Log.Message($"[HybridMP][SYNC] Draft command sent: Pawn {pawn.thingIDNumber}, Drafted={value}");
            }
            
            return true;
        }
    }
}
