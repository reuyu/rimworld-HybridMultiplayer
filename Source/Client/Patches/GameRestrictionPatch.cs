using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace HybridClient.Patches
{
    /// <summary>
    /// 게임 설정 제한 패치
    /// - 이야기꾼/난이도 변경 불가
    /// - 정착지 최대 2개 제한
    /// - 개발자 도구 비활성화 (첫 접속자 제외)
    /// </summary>
    
    /// <summary>
    /// 게임 내 이야기꾼/난이도 설정 페이지 닫힐 때 - 변경 무효화
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStorytellerInGame), nameof(Page_SelectStorytellerInGame.PreClose))]
    public static class BlockStorytellerChange_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // 연결 안됐으면 허용
            if (NetworkManager.Instance?.IsConnected != true)
                return true;
            
            // 서버에서 받은 설정으로 강제 복원
            var config = World.ClientWorldManager.Instance?.CurrentWorld;
            if (config != null)
            {
                // 이야기꾼 복원
                if (!string.IsNullOrEmpty(config.StorytellerDefName))
                {
                    var storytellerDef = DefDatabase<StorytellerDef>.GetNamedSilentFail(config.StorytellerDefName);
                    if (storytellerDef != null && Current.Game?.storyteller != null)
                    {
                        var difficultyDef = DefDatabase<DifficultyDef>.GetNamedSilentFail(config.DifficultyDefName) ?? DifficultyDefOf.Rough;
                        Current.Game.storyteller = new Storyteller(storytellerDef, difficultyDef);
                        Log.Message("[HybridMP] Storyteller/difficulty restored to server settings");
                    }
                }
            }
            
            // 메시지 표시
            Messages.Message("서버 설정으로 이야기꾼/난이도가 복원되었습니다.", MessageTypeDefOf.CautionInput);
            
            return true;
        }
    }
    
    /// <summary>
    /// 정착지 수 제한 (최대 2개)
    /// </summary>
    [HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
    public static class LimitSettlementCount_Patch
    {
        public const int MaxSettlements = 2;
        
        [HarmonyPrefix]
        public static bool Prefix(RimWorld.Planet.Caravan caravan)
        {
            // 연결 안됐으면 허용
            if (NetworkManager.Instance?.IsConnected != true)
                return true;
            
            // 현재 플레이어 정착지 수 확인
            int currentCount = 0;
            foreach (var settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == Faction.OfPlayer)
                    currentCount++;
            }
            
            if (currentCount >= MaxSettlements)
            {
                Messages.Message($"정착지는 최대 {MaxSettlements}개까지만 보유할 수 있습니다.", MessageTypeDefOf.RejectInput);
                return false; // 정착 취소
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// 정착지 생성 후 서버에 등록
    /// </summary>
    [HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
    public static class RegisterNewSettlement_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RimWorld.Planet.Caravan caravan)
        {
            // 연결 안됐으면 무시
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            // 새로 생성된 정착지 찾기 (캐러밴 위치)
            int tile = caravan.Tile;
            var settlement = Find.WorldObjects.SettlementAt(tile);
            
            if (settlement != null && settlement.Faction == Faction.OfPlayer)
            {
                Log.Message($"[HybridMP] New settlement created at tile {tile}, registering with server...");
                World.ClientWorldManager.Instance?.RequestSettlementCreate(tile, settlement.Name);
            }
        }
    }
    
    /// <summary>
    /// 개발자 도구 비활성화 (서버 접속 시)
    /// 테스트용: 임시 비활성화
    /// </summary>
    [HarmonyPatch(typeof(Prefs), nameof(Prefs.DevMode), MethodType.Setter)]
    public static class DisableDevMode_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool value)
        {
            // 테스트용: 개발자 모드 항상 허용
            return true;
            
            /*
            // 연결 안됐으면 허용
            if (NetworkManager.Instance?.IsConnected != true)
                return true;
            
            // TODO: 첫 접속자(월드 생성자)는 예외 처리 필요
            // 현재는 모든 클라이언트 개발자 모드 비활성화
            if (value == true)
            {
                Log.Message("[HybridMP] Dev mode blocked while connected to server");
                value = false;
            }
            
            return true;
            */
        }
    }
    
    /// <summary>
    /// 정착지 버리기 시 버려진 거주지 생성 방지 + 서버에 알림
    /// SettlementAbandonUtility.Abandon 패치 (Prefix로 원래 동작 방지)
    /// </summary>
    [HarmonyPatch(typeof(SettlementAbandonUtility), "Abandon")]
    public static class SettlementAbandon_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MapParent settlement)
        {
            // 연결 안됐으면 기본 동작 (버려진 거주지 생성됨)
            if (NetworkManager.Instance?.IsConnected != true)
                return true;
            
            // 플레이어 정착지만 처리
            if (settlement.Faction != Faction.OfPlayer)
                return true;
            
            Log.Message($"[HybridMP] Settlement abandoned at tile {settlement.Tile}, removing without creating abandoned settlement...");
            
            // 서버에 정착지 삭제 패킷 전송
            var packet = new HybridShared.Packets.SettlementRemovePacket
            {
                TileId = settlement.Tile
            };
            NetworkManager.Instance.Send(packet);
            
            // 맵이 있으면 맵도 삭제
            Map map = settlement.Map;
            if (map != null)
            {
                Current.Game.DeinitAndRemoveMap(map, true);
            }
            
            // 정착지 직접 삭제 (버려진 거주지 생성 안함)
            Find.WorldObjects.Remove(settlement);
            
            return false; // 원래 Abandon 동작 방지 (버려진 거주지 생성 안함)
        }
    }
    
    /// <summary>
    /// 정착지 삭제 시 서버에 알림 (Settlement.PostRemove) - 백업
    /// </summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.PostRemove))]
    public static class SettlementPostRemove_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Settlement __instance)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            // 플레이어 정착지만 처리
            if (__instance.Faction != Faction.OfPlayer)
                return;
            
            // 이미 Abandon에서 처리했을 수 있으므로 로그만 출력
            Log.Message($"[HybridMP] Settlement PostRemove at tile {__instance.Tile}");
        }
    }
}


