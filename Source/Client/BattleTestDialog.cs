using RimWorld;
using UnityEngine;
using Verse;
using HybridShared.Packets;

namespace HybridClient
{
    /// <summary>
    /// Battle Server 연결 테스트 다이얼로그
    /// </summary>
    public class BattleTestDialog : Window
    {
        private string ipAddress = "127.0.0.1";
        private string portString = "30000";
        private string chatMessage = "";
        private string lastTestResult = "";
        
        public override Vector2 InitialSize => new Vector2(450f, 450f);
        
        public BattleTestDialog()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            float curY = 0f;
            
            // 타이틀
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, curY, inRect.width, 35f), "Hybrid Multiplayer Test");
            Text.Font = GameFont.Small;
            curY += 45f;
            
            // 연결 상태
            var network = HybridMod.Network;
            string status = network != null && network.IsConnected 
                ? (network.IsAuthenticated ? $"Authenticated (ID: {network.SessionId})" : "Connected") 
                : "Disconnected";
            
            GUI.color = network?.IsConnected == true ? Color.green : Color.gray;
            Widgets.Label(new Rect(0, curY, inRect.width, 25f), $"Status: {status}");
            GUI.color = Color.white;
            curY += 30f;
            
            // IP 입력
            Widgets.Label(new Rect(0, curY, 80f, 25f), "Server IP:");
            ipAddress = Widgets.TextField(new Rect(90f, curY, 200f, 25f), ipAddress);
            curY += 30f;
            
            // 포트 입력
            Widgets.Label(new Rect(0, curY, 80f, 25f), "Port:");
            portString = Widgets.TextField(new Rect(90f, curY, 80f, 25f), portString);
            curY += 35f;
            
            // 연결 버튼
            float buttonWidth = 100f;
            
            if (Widgets.ButtonText(new Rect(0, curY, buttonWidth, 30f), "Connect"))
            {
                OnConnect();
            }
            
            if (Widgets.ButtonText(new Rect(buttonWidth + 10f, curY, buttonWidth, 30f), "Disconnect"))
            {
                OnDisconnect();
            }
            
            if (Widgets.ButtonText(new Rect((buttonWidth + 10f) * 2, curY, buttonWidth, 30f), "Ping"))
            {
                network?.SendPing();
            }
            
            curY += 40f;
            
            // 채팅 테스트
            Widgets.Label(new Rect(0, curY, 50f, 25f), "Chat:");
            chatMessage = Widgets.TextField(new Rect(55f, curY, 200f, 25f), chatMessage);
            if (Widgets.ButtonText(new Rect(260f, curY, 60f, 25f), "Send"))
            {
                if (!string.IsNullOrEmpty(chatMessage))
                {
                    network?.SendChat(chatMessage);
                    chatMessage = "";
                }
            }
            curY += 35f;
            
            // ========== Phase 3 테스트 ==========
            GUI.color = Color.cyan;
            Widgets.Label(new Rect(0, curY, inRect.width, 20f), "=== Phase 3 Tests ===");
            GUI.color = Color.white;
            curY += 25f;
            
            // ThingRegistry 테스트
            if (Widgets.ButtonText(new Rect(0, curY, 150f, 28f), "Test ThingRegistry"))
            {
                TestThingRegistry();
            }
            
            // MapSerializer 테스트
            if (Widgets.ButtonText(new Rect(160f, curY, 150f, 28f), "Test MapSerializer"))
            {
                TestMapSerializer();
            }
            curY += 35f;
            
            // PawnState 테스트
            if (Widgets.ButtonText(new Rect(0, curY, 150f, 28f), "Send PawnStates"))
            {
                TestSendPawnStates();
            }
            curY += 40f;
            
            // 테스트 결과 표시
            if (!string.IsNullOrEmpty(lastTestResult))
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(0, curY, inRect.width, 60f), lastTestResult);
                GUI.color = Color.white;
            }
            curY += 65f;
            
            // 닫기
            if (Widgets.ButtonText(new Rect(inRect.width / 2 - 50f, inRect.height - 35f, 100f, 30f), "Close"))
            {
                Close();
            }
        }
        
        private void OnConnect()
        {
            if (!int.TryParse(portString, out int port))
            {
                Messages.Message("Invalid port number", MessageTypeDefOf.RejectInput, false);
                return;
            }
            
            // 유저명으로 연결
            string username = Faction.OfPlayer?.Name ?? "Player";
            HybridMod.Network?.Connect(ipAddress, port, username);
        }
        
        private void OnDisconnect()
        {
            HybridMod.Network?.Disconnect();
        }
        
        private void TestThingRegistry()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                lastTestResult = "ERROR: No map loaded!";
                return;
            }
            
            ThingRegistry.Instance.Clear();
            ThingRegistry.Instance.RegisterMap(map);
            
            int pawnCount = 0;
            foreach (var pawn in map.mapPawns.AllPawns)
            {
                var found = ThingRegistry.Instance.GetPawn(pawn.thingIDNumber);
                if (found != null) pawnCount++;
            }
            
            lastTestResult = $"ThingRegistry OK!\n" +
                           $"Total Things: {ThingRegistry.Instance.Count}\n" +
                           $"Pawns verified: {pawnCount}";
            
            Log.Message($"[HybridMP] {lastTestResult.Replace("\n", " ")}");
        }
        
        private void TestMapSerializer()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                lastTestResult = "ERROR: No map loaded!";
                return;
            }
            
            var data = MapSerializer.SerializeMap(map);
            if (data == null)
            {
                lastTestResult = "ERROR: MapSerializer failed!";
                return;
            }
            
            // 압축 해제하여 원본 크기 확인
            var decompressed = MapSerializer.DeserializeMapData(data);
            
            lastTestResult = $"MapSerializer OK!\n" +
                           $"Map ID: {map.uniqueID}\n" +
                           $"Compressed: {data.Length:N0} bytes\n" +
                           $"Original: {decompressed?.Length ?? 0:N0} bytes";
            
            Log.Message($"[HybridMP] {lastTestResult.Replace("\n", " ")}");
        }
        
        private void TestSendPawnStates()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                lastTestResult = "ERROR: No map loaded!";
                return;
            }
            
            var network = HybridMod.Network;
            if (network == null || !network.IsAuthenticated)
            {
                lastTestResult = "ERROR: Not connected/authenticated!";
                return;
            }
            
            // 모든 플레이어 폰의 상태를 패킷으로 만들어 전송
            int count = 0;
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                var packet = new PawnStatePacket
                {
                    ThingID = pawn.thingIDNumber,
                    Position = new[] { (float)pawn.Position.x, (float)pawn.Position.y, (float)pawn.Position.z },
                    HealthPercent = pawn.health.summaryHealth.SummaryHealthPercent,
                    CurrentJobDefName = pawn.CurJob?.def?.defName ?? "None",
                    IsDrafted = pawn.Drafted,
                    DefName = pawn.def.defName
                };
                
                network.Send(packet);
                count++;
            }
            
            lastTestResult = $"Sent {count} PawnState packets!";
            Log.Message($"[HybridMP] {lastTestResult}");
        }
        
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            
            // 네트워크 업데이트
            HybridMod.Network?.Update();
        }
    }
}
