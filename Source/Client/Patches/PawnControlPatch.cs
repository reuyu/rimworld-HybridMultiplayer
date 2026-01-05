using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HybridClient.Patches
{
    /// <summary>
    /// InSync 중 폰 조종 권한 분리 패치
    /// MP MultifactionPatches 참조 - 자신의 세력 폰만 조종 가능
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PawnControlPatch
    {
        static PawnControlPatch()
        {
            var harmony = new Harmony("HybridClient.PawnControlPatch");
            harmony.PatchAll(typeof(PawnControlPatch).Assembly);
            Log.Message("[HybridMP] PawnControlPatch initialized");
        }
    }
    
    /// <summary>
    /// 폰 선택 시 권한 체크
    /// </summary>
    [HarmonyPatch(typeof(Selector), "Select")]
    public static class SelectorSelectPatch
    {
        public static bool Prefix(object obj)
        {
            // InSync가 아니면 기본 동작
            if (!InSync.InSyncManager.Instance.IsActive)
                return true;
            
            // 폰이 아니면 기본 동작
            if (obj is not Pawn pawn)
                return true;
            
            // 플레이어 세력 폰이 아니면 선택 허용 (적 등)
            if (pawn.Faction == null || !pawn.Faction.def.isPlayer)
                return true;
            
            // 자신의 세력 폰만 선택 허용
            if (!InSync.InSyncFactionManager.IsMyPawn(pawn))
            {
                // 다른 플레이어의 폰은 선택 불가
                Log.Message($"[HybridMP][PATCH] Cannot select other player's pawn: {pawn.Name}");
                Messages.Message("Cannot control other player's pawn", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// 드래프트 명령 통제
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    public static class DraftedPatch
    {
        public static bool Prefix(Pawn_DraftController __instance, bool value)
        {
            // InSync가 아니면 기본 동작
            if (!InSync.InSyncManager.Instance.IsActive)
                return true;
            
            var pawn = __instance.pawn;
            
            // 자신의 세력 폰만 드래프트 가능
            if (!InSync.InSyncFactionManager.IsMyPawn(pawn))
            {
                Log.Message($"[HybridMP][PATCH] Cannot draft other player's pawn: {pawn.Name}");
                return false;
            }
            
            // 명령 동기화
            InSync.SyncHandler.Instance.SyncDraft(pawn, value);
            
            return true;
        }
    }
    
    // FloatMenuFilterPatch 제거 - FloatMenuMakerMap.ChoicesAtFor 메서드 시그니처 불일치
    // RimWorld 버전에 따라 메서드 시그니처가 다를 수 있음
    // TODO: 필요시 정확한 메서드 시그니처 확인 후 구현
    
    // AutoUndraftPatch 제거 - Pawn_DraftController.pawn private 필드 접근 불가
    // TODO: 필요시 Harmony Transpiler 또는 다른 방법으로 구현
}

