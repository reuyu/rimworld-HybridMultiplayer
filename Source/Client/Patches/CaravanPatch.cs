using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace HybridClient.Patches
{
    /// <summary>
    /// 캐러밴 관련 Harmony 패치 - RT CaravanPatches 패턴 기반
    /// - Caravan.PostAdd: 캐러밴 생성 감지
    /// - Caravan.PostRemove: 캐러밴 삭제 감지
    /// - Caravan_PathFollower.TryEnterNextPathTile: 캐러밴 이동 감지 (Prefix + Postfix)
    /// </summary>
    
    /// <summary>
    /// 캐러밴 생성 감지
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.PostAdd))]
    public static class CaravanCreated_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            if (__instance.Faction == Faction.OfPlayer)
            {
                CaravanSync.ClientCaravanManager.Instance.OnCaravanCreated(__instance);
            }
        }
    }
    
    /// <summary>
    /// 캐러밴 삭제 감지 - 정착 시에도 호출됨
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.PostRemove))]
    public static class CaravanRemoved_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            if (__instance.Faction == Faction.OfPlayer)
            {
                CaravanSync.ClientCaravanManager.Instance.OnCaravanRemoved(__instance);
            }
        }
    }
    
    /// <summary>
    /// 캐러밴 이동 감지 - Prefix에서 이전 타일 저장, Postfix에서 변경 시 전송
    /// </summary>
    [HarmonyPatch(typeof(Caravan_PathFollower), "TryEnterNextPathTile")]
    public static class CaravanMoved_Patch
    {
        // 이동 전 타일 저장용
        [System.ThreadStatic]
        private static int previousTile;
        
        [HarmonyPrefix]
        public static void Prefix(Caravan ___caravan)
        {
            if (___caravan?.Faction == Faction.OfPlayer)
            {
                previousTile = ___caravan.Tile;
            }
        }
        
        [HarmonyPostfix]
        public static void Postfix(Caravan ___caravan)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            if (___caravan?.Faction == Faction.OfPlayer)
            {
                int currentTile = ___caravan.Tile;
                
                // 타일이 변경되었을 때만 전송
                if (currentTile != previousTile)
                {
                    Log.Message($"[HybridMP][CARAVAN] Moved from tile {previousTile} to {currentTile}");
                    CaravanSync.ClientCaravanManager.Instance.OnCaravanMoved(___caravan, currentTile);
                }
            }
        }
    }
    
    /// <summary>
    /// 캐러밴으로 정착 시 정착지 생성 - RT SettlementPatches 패턴
    /// Postfix에서 정착지 생성 패킷 전송
    /// 캐러밴 삭제는 Caravan.PostRemove에서 자동 처리됨
    /// </summary>
    [HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
    public static class SettleInEmptyTile_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan caravan)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            // 정착지 생성 패킷 전송
            Log.Message($"[HybridMP] Caravan settled at tile {caravan.Tile}, sending settlement create...");
            World.ClientWorldManager.Instance?.RequestSettlementCreate(caravan.Tile, "New Colony");
        }
    }
    
}
