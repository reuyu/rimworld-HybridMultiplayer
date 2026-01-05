using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using UnityEngine;
using HybridShared.Packets;

namespace HybridClient.Patches
{
    /// <summary>
    /// 캐러밴이 다른 플레이어 정착지에 도착 시 전투/진입 버튼 추가
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class CaravanGizmoPatch
    {
        // 로그 스팸 방지용 캐시
        private static int lastCheckedTile = -1;
        private static string lastOwnerUsername = null;
        private static bool lastCheckResult = false;
        
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Caravan __instance)
        {
            // 기존 Gizmo 반환
            foreach (var gizmo in gizmos)
            {
                yield return gizmo;
            }
            
            // 네트워크 연결 확인
            if (NetworkManager.Instance?.IsConnected != true)
                yield break;
            
            // 캐시된 결과 사용 (같은 타일이면 재계산 안함)
            int currentTile = __instance.Tile;
            string ownerUsername;
            
            if (currentTile == lastCheckedTile)
            {
                if (!lastCheckResult)
                    yield break;
                ownerUsername = lastOwnerUsername;
            }
            else
            {
                // 현재 타일의 다른 플레이어 정착지 확인
                ownerUsername = GetOtherPlayerSettlementOwner(currentTile);
                
                // 캐시 업데이트
                lastCheckedTile = currentTile;
                lastOwnerUsername = ownerUsername;
                lastCheckResult = !string.IsNullOrEmpty(ownerUsername);
                
                if (!lastCheckResult)
                    yield break;
            }
            
            // 해당 타일의 정착지 객체 가져오기
            Settlement settlement = null;
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is Settlement s && s.Tile == currentTile)
                {
                    settlement = s;
                    break;
                }
            }
            
            if (settlement == null)
                yield break;
            
            // 전투 버튼
            yield return new Command_Action
            {
                defaultLabel = "MP Attack",
                defaultDesc = $"Attack {ownerUsername}'s settlement (InSync mode)",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", true),
                action = () => RequestInSync(__instance, settlement, ownerUsername, InSyncMode.Battle)
            };
            
            // 진입 버튼 (협동)
            yield return new Command_Action
            {
                defaultLabel = "MP Enter",
                defaultDesc = $"Enter {ownerUsername}'s settlement peacefully (InSync mode)",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Trade", true),
                action = () => RequestInSync(__instance, settlement, ownerUsername, InSyncMode.Coop)
            };
        }
        
        /// <summary>
        /// ClientWorldManager에서 해당 타일의 다른 플레이어 정착지 소유자 찾기
        /// </summary>
        private static string GetOtherPlayerSettlementOwner(int tile)
        {
            var clientWorld = World.ClientWorldManager.Instance?.CurrentWorld;
            if (clientWorld?.PlayerSettlements == null)
                return null;
            
            string myUsername = NetworkManager.Instance?.Username;
            
            foreach (var info in clientWorld.PlayerSettlements)
            {
                if (info.TileId == tile && info.OwnerUsername != myUsername)
                {
                    return info.OwnerUsername;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// InSync 요청 전송
        /// </summary>
        private static void RequestInSync(Caravan caravan, Settlement settlement, string targetUsername, InSyncMode mode)
        {
            string modeText = mode == InSyncMode.Battle ? "attack" : "enter";
            Log.Message($"[HybridMP][INSYNC] Requesting to {modeText} {targetUsername}'s settlement at tile {settlement.Tile}");
            
            // 캐러밴 및 타일 정보 저장
            InSync.InSyncManager.Instance.SetPendingRequest(caravan, settlement.Tile);
            
            var packet = new InSyncRequestPacket
            {
                TargetTileId = settlement.Tile,
                TargetUsername = targetUsername,
                Mode = mode
            };
            
            NetworkManager.Instance.Send(packet);
            
            // 사용자에게 피드백
            Messages.Message($"Requesting to {modeText} {targetUsername}'s settlement...", MessageTypeDefOf.NeutralEvent, false);
        }
        
        /// <summary>
        /// 캐시 초기화 (타일 이동 시)
        /// </summary>
        public static void ClearCache()
        {
            lastCheckedTile = -1;
            lastOwnerUsername = null;
            lastCheckResult = false;
        }
    }
}
