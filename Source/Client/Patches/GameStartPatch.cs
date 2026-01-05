using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using HybridClient.World;

namespace HybridClient.Patches
{
    /// <summary>
    /// 게임 시작/초기화 패치 - 월드 전송 및 정착지 등록
    /// RT GameParameterManager 패턴 기반
    /// </summary>
    
    /// <summary>
    /// 게임 시작 시 월드가 새로 생성된 경우 서버에 전송
    /// </summary>
    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Game_InitNewGame_Patch
    {
        public static void Postfix()
        {
            Log.Message("[HybridMP][GAME] InitNewGame called");
            
            // 새 월드 생성 모드인 경우에만 서버에 전송
            if (ClientWorldManager.Instance.IsCreatingNewWorld)
            {
                Log.Message("[HybridMP][GAME] New world created - sending to server...");
                ClientWorldManager.Instance.SendWorldToServer();
            }
            
            // 연결된 상태라면 시작 정착지 등록
            if (NetworkManager.Instance?.IsConnected == true)
            {
                // 실제 생성된 정착지의 타일 가져오기 (Map의 Parent가 Settlement)
                int tile = -1;
                string settlementName = "Colony";
                
                // 방법 1: Find.GameInitData (아직 클리어 안됐을 경우)
                if (Find.GameInitData?.startingTile >= 0)
                {
                    tile = Find.GameInitData.startingTile;
                }
                
                // 방법 2: 플레이어 정착지에서 가져오기
                if (tile < 0)
                {
                    var playerSettlement = Find.WorldObjects.Settlements
                        .FirstOrDefault(s => s.Faction != null && s.Faction.IsPlayer);
                    if (playerSettlement != null)
                    {
                        tile = playerSettlement.Tile;
                        settlementName = playerSettlement.Name;
                    }
                }
                
                // 방법 3: 첫 번째 맵의 타일
                if (tile < 0 && Find.Maps?.Count > 0)
                {
                    tile = Find.Maps[0].Tile;
                }
                
                if (tile >= 0)
                {
                    settlementName = Faction.OfPlayer?.Name ?? settlementName;
                    Log.Message($"[HybridMP][GAME] Registering starting settlement at tile {tile}, name: {settlementName}");
                    ClientWorldManager.Instance.RequestSettlementCreate(tile, settlementName);
                }
                else
                {
                    Log.Warning("[HybridMP][GAME] Could not find starting tile for settlement registration");
                }
            }
        }
    }
}
