using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace HybridClient.Patches
{
    /// <summary>
    /// 게임 로드 완료 후 정착지 동기화 - RT GameStatusPatches 패턴 기반
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public static class GameLoadComplete_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            Log.Message("[HybridMP] Game loaded - syncing world objects from server...");
            
            // 서버에서 정착지 및 캐러밴 동기화
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                World.ClientWorldManager.Instance?.SyncSettlementsAfterLoad();
                World.ClientWorldManager.Instance?.SyncCaravansAfterLoad();
                
                // 플레이어 캐러밴 추적 시작 (세이브에 있던 캐러밴들)
                CaravanSync.ClientCaravanManager.Instance.TrackAllPlayerCaravans();
                
                // 유저의 정착지로 카메라 전환
                SelectPlayerSettlement();
            });
        }
        
        /// <summary>
        /// 유저의 정착지를 찾아서 해당 맵으로 카메라 전환
        /// </summary>
        private static void SelectPlayerSettlement()
        {
            string username = NetworkManager.Instance?.Username;
            if (string.IsNullOrEmpty(username))
            {
                Log.Warning("[HybridMP][WORLD] No username - cannot select settlement");
                return;
            }
            
            // ClientWorldManager에서 유저의 정착지 타일 찾기
            var clientWorld = World.ClientWorldManager.Instance?.CurrentWorld;
            int? playerTile = null;
            
            if (clientWorld?.PlayerSettlements != null)
            {
                foreach (var info in clientWorld.PlayerSettlements)
                {
                    if (info.OwnerUsername == username)
                    {
                        playerTile = info.TileId;
                        Log.Message($"[HybridMP][WORLD] Found player settlement at tile {playerTile}");
                        break;
                    }
                }
            }
            
            if (playerTile != null)
            {
                // 해당 타일의 맵 찾기
                foreach (var map in Find.Maps)
                {
                    if (map.Tile == playerTile.Value)
                    {
                        Log.Message($"[HybridMP][WORLD] Switching to player map at tile {playerTile}");
                        Current.Game.CurrentMap = map;
                        SelectFirstPawn(map);
                        return;
                    }
                }
                
                Log.Warning($"[HybridMP][WORLD] Map not found for player tile {playerTile}");
            }
            
            // 정착지를 찾지 못한 경우 첫 번째 맵 사용
            if (Find.Maps != null && Find.Maps.Count > 0)
            {
                var map = Find.Maps[0];
                Log.Message($"[HybridMP][WORLD] Using first available map: {map.uniqueID}");
                Current.Game.CurrentMap = map;
                SelectFirstPawn(map);
            }
        }
        
        /// <summary>
        /// 맵에서 첫 번째 플레이어 폰 선택
        /// </summary>
        private static void SelectFirstPawn(Map map)
        {
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn);
                CameraJumper.TryJumpAndSelect(pawn);
                Log.Message($"[HybridMP][WORLD] Selected pawn: {pawn.LabelShort}");
                return;
            }
        }
    }
    
    /// <summary>
    /// 새 게임 시작 시 정착지 등록 - RT GameStatusPatches 패턴 기반
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class GameInitNewGame_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Game __instance)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            // 새 게임 시작 시 자동 저장
            Log.Message("[HybridMP] New game initialized - auto saving...");
            
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // 새 정착지를 서버에 등록하고 저장
                var map = __instance.CurrentMap;
                if (map != null)
                {
                    World.ClientWorldManager.Instance?.RequestSettlementCreate(map.Tile, map.info.parent?.Label ?? "New Colony");
                }
            });
        }
    }
}
