using HarmonyLib;
using Verse;
using RimWorld;
using HybridClient.InSync;
using HybridShared.Packets;

namespace HybridClient.Patches
{
    /// <summary>
    /// InSync 모드 틱 제어 패치
    /// MP TickPatch 참조 - InSync 활성화 시 기본 틱 차단하고 LockstepController.Tick() 호출
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    public static class TickManagerPatch
    {
        /// <summary>
        /// DoSingleTick Prefix - InSync 활성화 시 기본 틱 차단
        /// </summary>
        public static bool Prefix(TickManager __instance)
        {
            // InSync가 활성화되지 않았으면 기본 동작
            if (!LockstepController.Instance.IsActive)
                return true;
            
            // InSync 활성화 시 기본 틱 차단
            // LockstepController가 틱을 제어
            return false;
        }
    }
    
    /// <summary>
    /// TickManager Update 패치 - Lockstep 틱 실행
    /// MP TickPatch.TickManagerUpdate 참조
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickManagerUpdatePatch
    {
        public static void Postfix(TickManager __instance)
        {
            // InSync가 활성화되지 않았으면 무시
            if (!LockstepController.Instance.IsActive)
                return;
            
            // Lockstep 틱 실행
            try
            {
                LockstepController.Instance.Tick();
            }
            catch (System.Exception e)
            {
                Log.Error($"[HybridMP][TICK] Lockstep tick error: {e}");
            }
        }
    }
    
    /// <summary>
    /// 시간 속도 변경 방지 패치 (InSync 중)
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.CurTimeSpeed), MethodType.Setter)]
    public static class TimeSpeedPatch
    {
        public static bool Prefix(TickManager __instance, TimeSpeed value)
        {
            // InSync가 활성화되지 않았으면 기본 동작
            if (!LockstepController.Instance.IsActive)
                return true;
            
            // InSync 중에는 시간 속도 변경 불가 (전투 모드)
            if (LockstepController.Instance.Mode == InSyncMode.Battle)
            {
                Log.Message("[HybridMP][TICK] Time speed change blocked during InSync battle");
                return false;
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// 일시정지 방지 패치 (InSync 전투 모드 중)
    /// </summary>
    [HarmonyPatch(typeof(TickManager), "Pause")]
    public static class PausePatch
    {
        public static bool Prefix()
        {
            if (!LockstepController.Instance.IsActive)
                return true;
            
            // 전투 모드에서는 일시정지 불가
            if (LockstepController.Instance.Mode == InSyncMode.Battle)
            {
                Messages.Message("Cannot pause during InSync battle", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            
            return true;
        }
    }
}
