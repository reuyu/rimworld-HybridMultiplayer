using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;
using HybridShared.Packets;

namespace HybridClient
{
    /// <summary>
    /// 하이브리드 멀티플레이어 클라이언트 모드 진입점
    /// </summary>
    [StaticConstructorOnStartup]
    public class HybridMod : Mod
    {
        public static HybridMod Instance { get; private set; }
        public static Harmony HarmonyInstance { get; private set; }
        
        // 네트워크 매니저
        public static NetworkManager Network { get; private set; }
        
        // 테스트 결과
        private static string lastTestResult = "";
        private static string serverIp = "127.0.0.1";
        private static string serverPort = "30000";
        private static string chatMessage = "";
        
        public HybridMod(ModContentPack content) : base(content)
        {
            Instance = this;
            
            // Harmony 패치 적용
            HarmonyInstance = new Harmony("hybridmp.rimworld.multiplayer");
            HarmonyInstance.PatchAll();
            
            // 네트워크 매니저 초기화
            Network = new NetworkManager();
            
            Log.Message("[HybridMP] Hybrid Multiplayer mod initialized!");
        }
        
        public override string SettingsCategory() => "Hybrid Multiplayer";
        
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            
            // ======= 연결 설정 =======
            listing.Label("=== Connection ===");
            listing.Gap(5f);
            
            // IP/Port
            serverIp = listing.TextEntryLabeled("Server IP: ", serverIp);
            serverPort = listing.TextEntryLabeled("Port: ", serverPort);
            listing.Gap(5f);
            
            // 연결 상태
            var network = Network;
            string status = network?.IsConnected == true 
                ? (network.IsAuthenticated ? $"✓ Authenticated (ID: {network.SessionId})" : "✓ Connected") 
                : "✗ Disconnected";
            listing.Label($"Status: {status}");
            listing.Gap(5f);
            
            // 연결 버튼
            if (listing.ButtonText("Connect"))
            {
                if (int.TryParse(serverPort, out int port))
                {
                    string username = Faction.OfPlayer?.Name ?? "Player";
                    network?.Connect(serverIp, port, username);
                }
            }
            
            if (listing.ButtonText("Disconnect"))
            {
                network?.Disconnect();
            }
            
            listing.Gap(10f);
            
            // ======= 채팅 테스트 =======
            listing.Label("=== Chat Test ===");
            chatMessage = listing.TextEntry(chatMessage);
            if (listing.ButtonText("Send Chat") && !string.IsNullOrEmpty(chatMessage))
            {
                network?.SendChat(chatMessage);
                chatMessage = "";
            }
            
            listing.Gap(10f);
            
            // ======= Phase 3 테스트 =======
            listing.Label("=== Phase 3 Tests ===");
            listing.Gap(5f);
            
            if (listing.ButtonText("Test ThingRegistry"))
            {
                TestThingRegistry();
            }
            
            if (listing.ButtonText("Test MapSerializer"))
            {
                TestMapSerializer();
            }
            
            if (listing.ButtonText("Send PawnStates"))
            {
                TestSendPawnStates();
            }
            
            listing.Gap(10f);
            
            // ======= 델타 동기화 테스트 =======
            listing.Label("=== Delta Sync Tests ===");
            listing.Gap(5f);
            
            if (listing.ButtonText("Test Delta Sync"))
            {
                TestDeltaSync();
            }
            
            if (listing.ButtonText("Send Client State"))
            {
                TestSendClientState();
            }
            
            listing.Gap(10f);
            
            // 테스트 결과
            if (!string.IsNullOrEmpty(lastTestResult))
            {
                listing.Label("Result:");
                listing.Label(lastTestResult);
            }
            
            listing.End();
            
            // 네트워크 업데이트
            network?.Update();
        }
        
        private static void TestThingRegistry()
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
            
            lastTestResult = $"OK! Things: {ThingRegistry.Instance.Count}, Pawns: {pawnCount}";
            Log.Message($"[HybridMP] ThingRegistry: {lastTestResult}");
        }
        
        private static void TestMapSerializer()
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
            
            var decompressed = MapSerializer.DeserializeMapData(data);
            
            lastTestResult = $"OK! MapID: {map.uniqueID}, Compressed: {data.Length:N0}B, Original: {decompressed?.Length ?? 0:N0}B";
            Log.Message($"[HybridMP] MapSerializer: {lastTestResult}");
        }
        
        private static void TestSendPawnStates()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                lastTestResult = "ERROR: No map loaded!";
                return;
            }
            
            var network = Network;
            if (network == null || !network.IsAuthenticated)
            {
                lastTestResult = "ERROR: Not connected!";
                return;
            }
            
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
        
        private static void TestDeltaSync()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                lastTestResult = "ERROR: No map loaded!";
                return;
            }
            
            // 현재 상태 캡처
            var currentState = DeltaSyncManager.Instance.CaptureMapState(map);
            
            // 변경 감지
            var deltas = DeltaSyncManager.Instance.DetectChanges(currentState);
            
            // 상태 저장 (다음 비교용)
            DeltaSyncManager.Instance.UpdateLastSentState(currentState);
            
            int thingCount = currentState.Count;
            int pawnCount = currentState.Count(s => s.IsPawn);
            int deltaCount = deltas.Count;
            
            lastTestResult = $"OK! Things: {thingCount}, Pawns: {pawnCount}, Deltas: {deltaCount}";
            
            if (deltaCount > 0)
            {
                var types = deltas.GroupBy(d => d.Type)
                                 .Select(g => $"{g.Key}:{g.Count()}")
                                 .ToArray();
                lastTestResult += $"\n({string.Join(", ", types)})";
            }
            
            Log.Message($"[HybridMP] DeltaSync: {lastTestResult.Replace("\n", " ")}");
        }
        
        private static void TestSendClientState()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                lastTestResult = "ERROR: No map loaded!";
                return;
            }
            
            var network = Network;
            if (network == null || !network.IsAuthenticated)
            {
                lastTestResult = "ERROR: Not connected!";
                return;
            }
            
            // 클라이언트 상태 패킷 생성
            var packet = DeltaSyncManager.Instance.CreateClientStatePacket(map, Find.TickManager.TicksGame);
            
            // 전송
            network.Send(packet);
            
            lastTestResult = $"Sent ClientState! Things: {packet.Things.Count}, Hash: {packet.StateHash:X8}";
            Log.Message($"[HybridMP] {lastTestResult}");
        }
    }
    
    /// <summary>
    /// 게임 시작 시 초기화
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HybridStartup
    {
        static HybridStartup()
        {
            Log.Message("[HybridMP] StaticConstructorOnStartup completed");
        }
    }
}
