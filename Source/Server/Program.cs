using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using HybridShared;
using HybridShared.Packets;
using HybridServer.Handlers;
using HybridServer.Battle;
using HybridServer.Lobby;

namespace HybridServer
{
    /// <summary>
    /// 하이브리드 멀티플레이어 서버 메인
    /// </summary>
    class Program
    {
        static ServerNetworkManager server;
        static bool running = true;
        
        static void Main(string[] args)
        {
            Console.Title = "Hybrid Multiplayer Server";
            Console.WriteLine("========================================");
            Console.WriteLine("   Hybrid Multiplayer Server v0.2");
            Console.WriteLine("========================================");
            Console.WriteLine();
            
            int port = 30000;
            if (args.Length > 0 && int.TryParse(args[0], out int customPort))
            {
                port = customPort;
            }
            
            server = new ServerNetworkManager(port);
            server.Start();
            
            Console.WriteLine($"Server started on port {port}");
            Console.WriteLine("Commands: quit, list, kick <id>, say <message>, battle start, battle list, battle end <id>");
            Console.WriteLine();
            
            // 메인 루프
            while (running)
            {
                // 네트워크 업데이트
                server.Update();
                
                // 콘솔 입력 처리
                if (Console.KeyAvailable)
                {
                    string input = Console.ReadLine();
                    ProcessCommand(input);
                }
                
                System.Threading.Thread.Sleep(15); // ~60Hz
            }
            
            server.Stop();
            Console.WriteLine("Server stopped.");
        }
        
        static void ProcessCommand(string input)
        {
            if (string.IsNullOrEmpty(input)) return;
            
            string[] parts = input.Split(' ');
            string cmd = parts[0].ToLower();
            
            switch (cmd)
            {
                case "quit":
                case "exit":
                    running = false;
                    break;
                    
                case "list":
                    Console.WriteLine($"Connected clients: {server.ClientCount}");
                    foreach (var (clientId, info) in server.GetClientInfos())
                    {
                        Console.WriteLine($"  - ID:{clientId} {info.Username ?? "???"} ({info.Peer.EndPoint}) Ping:{info.Peer.Ping}ms");
                    }
                    break;
                    
                case "kick":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int kickId))
                    {
                        server.KickClient(kickId);
                    }
                    break;
                    
                case "say":
                    if (parts.Length > 1)
                    {
                        string message = string.Join(" ", parts.Skip(1));
                        server.BroadcastChat("[SERVER]", message, ChatMessageType.Broadcast);
                        Console.WriteLine($"[SERVER] {message}");
                    }
                    break;
                    
                case "battle":
                    HandleBattleCommand(parts);
                    break;
                    
                default:
                    Console.WriteLine($"Unknown command: {cmd}");
                    break;
            }
        }
        
        static void HandleBattleCommand(string[] parts)
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("Usage: battle start | battle list | battle end <id>");
                return;
            }
            
            string subCmd = parts[1].ToLower();
            switch (subCmd)
            {
                case "start":
                    var participants = server.GetClientInfos()
                        .Where(c => c.Info.Authenticated)
                        .Select(c => c.Id)
                        .ToArray();
                    
                    if (participants.Length >= 2)
                    {
                        var session = server.StartBattle(participants);
                        if (session != null)
                        {
                            Console.WriteLine($"[BATTLE] Created: {session.BattleId}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[BATTLE] Need at least 2 authenticated clients");
                    }
                    break;
                    
                case "list":
                    server.PrintBattleStatus();
                    break;
                    
                case "end":
                    if (parts.Length > 2)
                    {
                        server.EndBattle(parts[2], BattleResult.Aborted);
                    }
                    else
                    {
                        Console.WriteLine("Usage: battle end <battleId>");
                    }
                    break;
                    
                case "force":
                    // 디버그: 클라이언트 Ready 없이 강제로 전투 시작
                    if (parts.Length > 2)
                    {
                        server.ForceBattleStart(parts[2]);
                    }
                    else
                    {
                        // battleId 없으면 가장 최근 전투 강제 시작
                        server.ForceBattleStart(null);
                    }
                    break;
                    
                case "desync":
                    // 기술 검증: Desync 시뮬레이션 → 델타 동기화 테스트
                    if (parts.Length > 2)
                    {
                        server.SimulateDesync(parts[2]);
                    }
                    else
                    {
                        server.SimulateDesync(null);
                    }
                    break;
                    
                default:
                    Console.WriteLine($"Unknown battle subcommand: {subCmd}");
                    Console.WriteLine("Available: start, list, end <id>, force [id], desync [id]");
                    break;
            }
        }
    }
    
    /// <summary>
    /// 클라이언트 정보
    /// </summary>
    public class ClientInfo
    {
        public NetPeer Peer { get; set; }
        public string Username { get; set; }
        public bool Authenticated { get; set; }
        public DateTime ConnectTime { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// 서버 네트워크 매니저
    /// </summary>
    public class ServerNetworkManager : INetEventListener
    {
        private NetManager server;
        private int port;
        private Dictionary<int, ClientInfo> clients = new();
        private Dictionary<NetPeer, int> peerToId = new();
        private int nextClientId = 1;
        private ServerPacketRouter router;
        private BattleManager battleManager;
        
        public int ClientCount => clients.Count;
        public string ServerName { get; set; } = "Hybrid MP Server";
        
        public ServerNetworkManager(int port)
        {
            this.port = port;
            server = new NetManager(this);
            
            // 패킷 핸들러 등록
            router = new ServerPacketRouter();
            RegisterPacketHandlers();
            
            // 전투 매니저 초기화
            battleManager = new BattleManager();
            SetupBattleEvents();
        }
        
        private void RegisterPacketHandlers()
        {
            router.Register<HandshakePacket>(HandleHandshake);
            router.Register<PingPacket>(HandlePing);
            router.Register<ChatPacket>(HandleChat);
            router.Register<PawnStatePacket>(HandlePawnState);
            
            // 월드/정착지 패킷 핸들러
            router.Register<WorldPacket>(HandleWorld);
            router.Register<SettlementCreatePacket>(HandleSettlementCreate);
            router.Register<SettlementRemovePacket>(HandleSettlementRemove);
            
            // 캐러밴 패킷 핸들러
            router.Register<CaravanUpdatePacket>(HandleCaravanUpdate);
            
            // 세이브 패킷 핸들러
            router.Register<SaveUploadPacket>(HandleSaveUpload);
            router.Register<SaveRequestPacket>(HandleSaveRequest);
            
            // 전투 패킷 핸들러
            router.Register<BattleReadyPacket>(HandleBattleReady);
            router.Register<BattleActionPacket>(HandleBattleAction);
            router.Register<BattleStateHashPacket>(HandleBattleStateHash);
            
            // InSync 패킷 핸들러
            router.Register<InSyncRequestPacket>(HandleInSyncRequest);
            router.Register<MapSnapshotPacket>(HandleMapSnapshot);
            router.Register<LockstepCommandPacket>(HandleLockstepCommand);
            router.Register<InSyncEndPacket>(HandleInSyncEnd);
        }
        
        private void SetupBattleEvents()
        {
            // 전투 시작 시 참가자들에게 패킷 전송
            battleManager.OnBattleStart += (session, packet) =>
            {
                foreach (var clientId in session.ParticipantClientIds)
                {
                    if (clients.TryGetValue(clientId, out var info))
                    {
                        Send(info.Peer, packet);
                    }
                }
            };
            
            // 틱 동기화 패킷 브로드캐스트
            battleManager.OnBroadcastSync += (battleId, packet) =>
            {
                var session = battleManager.GetBattle(battleId);
                if (session == null) return;
                
                foreach (var clientId in session.ParticipantClientIds)
                {
                    if (clients.TryGetValue(clientId, out var info))
                    {
                        Send(info.Peer, packet);
                    }
                }
            };
            
            // Fast Resync 패킷 전송
            battleManager.OnSendResync += (battleId, clientId, packet) =>
            {
                if (clients.TryGetValue(clientId, out var info))
                {
                    Console.WriteLine($"[RESYNC] Sending to client {clientId}");
                    Send(info.Peer, packet);
                }
            };
            
            // 전투 종료 패킷 전송
            battleManager.OnBattleEnd += (battleId, packet) =>
            {
                var session = battleManager.GetBattle(battleId);
                if (session == null) return;
                
                foreach (var clientId in session.ParticipantClientIds)
                {
                    if (clients.TryGetValue(clientId, out var info))
                    {
                        Send(info.Peer, packet);
                    }
                }
            };
        }
        
        #region Packet Handlers
        
        private void HandleHandshake(NetPeer peer, HandshakePacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            // 유저명 저장
            clientInfo.Username = packet.Username;
            clientInfo.Authenticated = true;
            
            Console.WriteLine($"[AUTH] {packet.Username} authenticated (Proto v{packet.ProtocolVersion})");
            
            // 응답 전송
            var response = HandshakeResponsePacket.CreateSuccess(
                clientId, 
                ServerName, 
                ClientCount
            );
            Send(peer, response);
            
            // 다른 클라이언트들에게 알림
            BroadcastChat("[SERVER]", $"{packet.Username} joined the server.", ChatMessageType.System);
            
            // ========== 월드 전송 (RT PostLogin 패턴) ==========
            PostLogin(peer, clientId, packet.Username);
        }
        
        /// <summary>
        /// 로그인 후 처리 - RT LoginManager.PostLogin 패턴
        /// </summary>
        private void PostLogin(NetPeer peer, int clientId, string username)
        {
            Console.WriteLine($"[POSTLOGIN] Processing for {username}...");
            
            // 월드 존재 확인
            if (WorldManager.Instance.HasWorld)
            {
                // 기존 월드 전송
                Console.WriteLine($"[POSTLOGIN] Sending existing world to {username}");
                var worldPacket = WorldManager.Instance.CreateWorldSendPacket();
                if (worldPacket != null)
                {
                    Send(peer, worldPacket);
                }
                
                // 정착지 목록 전송
                var settlementPacket = new SettlementListPacket
                {
                    Settlements = WorldManager.Instance.CurrentWorld.PlayerSettlements
                };
                Send(peer, settlementPacket);
                Console.WriteLine($"[POSTLOGIN] Sent {settlementPacket.Settlements?.Count ?? 0} settlements to {username}");
                
                // 캐러밴 목록 전송
                var caravanPacket = new CaravanListPacket
                {
                    Caravans = WorldManager.Instance.CurrentWorld.PlayerCaravans
                };
                Send(peer, caravanPacket);
                Console.WriteLine($"[POSTLOGIN] Sent {caravanPacket.Caravans?.Count ?? 0} caravans to {username}");
            }
            else
            {
                // 첫 접속자 - 월드 생성 요청
                Console.WriteLine($"[POSTLOGIN] No world exists - requesting {username} to create world");
                var requestPacket = WorldManager.Instance.CreateWorldRequestPacket();
                Send(peer, requestPacket);
            }
        }
        
        private void HandlePing(NetPeer peer, PingPacket packet)
        {
            var pong = new PongPacket(packet.Timestamp);
            Send(peer, pong);
        }
        
        private void HandleChat(NetPeer peer, ChatPacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated)
            {
                Console.WriteLine($"[!] Unauthenticated chat from {peer.EndPoint}");
                return;
            }
            
            // 발신자 이름 설정
            packet.SenderName = clientInfo.Username;
            
            Console.WriteLine($"[CHAT] {packet.SenderName}: {packet.Message}");
            
            // 모든 클라이언트에게 브로드캐스트
            Broadcast(packet);
        }
        
        private void HandleWorld(NetPeer peer, WorldPacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            // 클라이언트가 생성한 월드 수신
            if (packet.StepMode == WorldStepMode.SendToServer)
            {
                Console.WriteLine($"[WORLD] Receiving world from {clientInfo.Username}");
                WorldManager.Instance.ReceiveWorldFromClient(packet.WorldData, clientInfo.Username);
                
                // 다른 클라이언트들에게 알림
                BroadcastChat("[SERVER]", $"{clientInfo.Username} created the world!", ChatMessageType.System);
            }
        }
        
        private void HandleSettlementCreate(NetPeer peer, SettlementCreatePacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[SETTLEMENT] {clientInfo.Username} requests settlement at tile {packet.TileId}");
            
            var response = WorldManager.Instance.HandleSettlementCreate(clientId, clientInfo.Username, packet);
            Send(peer, response);
            
            if (response.Success)
            {
                // 모든 클라이언트에게 정착지 목록 업데이트 전송
                var listPacket = WorldManager.Instance.CreateSettlementListPacket();
                Broadcast(listPacket);
                
                BroadcastChat("[SERVER]", $"{clientInfo.Username} founded {response.Settlement?.SettlementName}!", ChatMessageType.System);
            }
        }
        
        private void HandleSaveUpload(NetPeer peer, SaveUploadPacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[SAVE] Receiving save from {clientInfo.Username} ({packet.SaveData?.Length ?? 0} bytes)");
            SaveManager.Instance.HandleSaveUpload(clientInfo.Username, packet);
        }
        
        private void HandleSaveRequest(NetPeer peer, SaveRequestPacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[SAVE] {clientInfo.Username} requesting save...");
            var downloadPacket = SaveManager.Instance.CreateSaveDownloadPacket(clientInfo.Username);
            
            if (downloadPacket.HasSave)
            {
                Console.WriteLine($"[SAVE] Sending save to {clientInfo.Username} ({downloadPacket.SaveData?.Length ?? 0} bytes)");
            }
            else
            {
                Console.WriteLine($"[SAVE] No save found for {clientInfo.Username}");
            }
            
            Send(peer, downloadPacket);
        }
        
        // ========== 캐러밴 핸들러 ==========
        
        private void HandleCaravanUpdate(NetPeer peer, CaravanUpdatePacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            string username = clientInfo.Username;
            var world = WorldManager.Instance.CurrentWorld;
            
            if (world == null) return;
            
            // 기존 캐러밴 목록에서 해당 유저의 캐러밴 찾기
            var caravans = world.PlayerCaravans;
            
            switch (packet.StepMode)
            {
                case CaravanStepMode.Add:
                    // 기존에 같은 ID가 있으면 업데이트
                    var existingAdd = caravans.Find(c => c.CaravanId == packet.Caravan.CaravanId && c.OwnerUsername == username);
                    if (existingAdd == null)
                    {
                        packet.Caravan.OwnerUsername = username;
                        caravans.Add(packet.Caravan);
                    }
                    Console.WriteLine($"[CARAVAN] {username} created caravan at tile {packet.Caravan.Tile}");
                    WorldManager.Instance.SaveWorld();
                    break;
                    
                case CaravanStepMode.Remove:
                    caravans.RemoveAll(c => c.CaravanId == packet.Caravan.CaravanId && c.OwnerUsername == username);
                    Console.WriteLine($"[CARAVAN] {username} removed caravan");
                    WorldManager.Instance.SaveWorld();
                    break;
                    
                case CaravanStepMode.Move:
                    var existing = caravans.Find(c => c.CaravanId == packet.Caravan.CaravanId && c.OwnerUsername == username);
                    if (existing != null)
                    {
                        existing.Tile = packet.Caravan.Tile;
                    }
                    // 이동은 너무 자주 저장하면 성능 문제 - 주기적 저장 또는 연결 해제 시 저장
                    break;
            }
            
            // 다른 클라이언트들에게 브로드캐스트 (발신자 제외)
            foreach (var kvp in clients)
            {
                if (kvp.Value.Authenticated && kvp.Value.Peer != null && kvp.Value.Peer != peer)
                {
                    Send(kvp.Value.Peer, packet);
                }
            }
        }
        
        // ========== 정착지 삭제 핸들러 ==========
        
        private void HandleSettlementRemove(NetPeer peer, SettlementRemovePacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[SETTLEMENT] {clientInfo.Username} removing settlement at tile {packet.TileId}");
            
            // WorldManager에서 정착지 삭제
            bool removed = WorldManager.Instance.RemoveSettlement(clientInfo.Username, packet.TileId);
            
            if (removed)
            {
                // 다른 클라이언트들에게 삭제된 정착지 알림 (발신자 제외)
                foreach (var kvp in clients)
                {
                    if (kvp.Value.Authenticated && kvp.Value.Peer != null && kvp.Value.Peer != peer)
                    {
                        Send(kvp.Value.Peer, packet); // SettlementRemovePacket 전송
                    }
                }
                Console.WriteLine($"[SETTLEMENT] Broadcasted settlement remove at tile {packet.TileId} to other clients");
                
                BroadcastChat("[SERVER]", $"{clientInfo.Username}'s settlement was abandoned.", ChatMessageType.System);
            }
        }
        
        private void HandlePawnState(NetPeer peer, PawnStatePacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[PAWN] {clientInfo.Username}: ThingID={packet.ThingID}, " +
                            $"Pos=({packet.Position[0]:F1},{packet.Position[1]:F1},{packet.Position[2]:F1}), " +
                            $"HP={packet.HealthPercent:P0}, Job={packet.CurrentJobDefName}, Drafted={packet.IsDrafted}");
        }
        
        private void HandleBattleReady(NetPeer peer, BattleReadyPacket packet)
        {
            int clientId = peerToId[peer];
            Console.WriteLine($"[BATTLE][READY] Client {clientId} ready for battle {packet.BattleId}");
            battleManager.HandleClientReady(packet.BattleId, clientId);
        }
        
        private void HandleBattleAction(NetPeer peer, BattleActionPacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[BATTLE][ACTION] {clientInfo.Username}: {packet.Action}");
            battleManager.HandleAction(packet.BattleId, packet.Action);
        }
        
        private void HandleBattleStateHash(NetPeer peer, BattleStateHashPacket packet)
        {
            int clientId = peerToId[peer];
            Console.WriteLine($"[BATTLE][HASH] Client {clientId}: Tick {packet.Tick}, Hash 0x{packet.StateHash:X8}");
            battleManager.HandleClientHash(packet.BattleId, clientId, packet.Tick, packet.StateHash);
        }
        
        #endregion
        
        public void Start()
        {
            server.Start(port);
        }
        
        public void Stop()
        {
            server.Stop();
        }
        
        public void Update()
        {
            server.PollEvents();
            battleManager?.Update();
        }
        
        public IEnumerable<(int Id, ClientInfo Info)> GetClientInfos()
        {
            return clients.Select(kvp => (kvp.Key, kvp.Value));
        }
        
        public void KickClient(int id)
        {
            if (clients.TryGetValue(id, out var info))
            {
                info.Peer.Disconnect();
                Console.WriteLine($"Kicked client {id} ({info.Username})");
            }
        }
        
        public void Send(NetPeer peer, PacketBase packet, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
        {
            peer.Send(packet.Serialize(), method);
        }
        
        public void Broadcast(PacketBase packet, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
        {
            byte[] data = packet.Serialize();
            foreach (var info in clients.Values)
            {
                info.Peer.Send(data, method);
            }
        }
        
        public void BroadcastChat(string sender, string message, ChatMessageType type = ChatMessageType.Normal)
        {
            var chat = new ChatPacket(message, type)
            {
                SenderName = sender
            };
            Broadcast(chat);
        }
        
        #region Battle Management
        
        public BattleSession StartBattle(int[] participantIds)
        {
            return battleManager.CreateBattle(participantIds);
        }
        
        public void EndBattle(string battleId, BattleResult result)
        {
            battleManager.EndBattle(battleId, result);
        }
        
        public void PrintBattleStatus()
        {
            battleManager.PrintStatus();
        }
        
        public void ForceBattleStart(string battleId)
        {
            battleManager.ForceStart(battleId);
        }
        
        /// <summary>
        /// 기술 검증: Desync 시뮬레이션
        /// 가짜 델타 데이터를 생성하여 클라이언트에 전송
        /// </summary>
        public void SimulateDesync(string battleId)
        {
            // 전투 세션 찾기
            var session = string.IsNullOrEmpty(battleId) 
                ? battleManager.GetActiveBattle() 
                : battleManager.GetBattle(battleId);
            
            if (session == null)
            {
                Console.WriteLine("[DESYNC-TEST] No active battle found");
                return;
            }
            
            Console.WriteLine($"[DESYNC-TEST] Simulating desync for battle {session.BattleId}");
            
            // 가짜 델타 데이터 생성
            var resyncPacket = new AuthoritativeStatePacket
            {
                ServerTick = session.CurrentTick,
                Corrections = new List<ThingDeltaData>
                {
                    // 위치 수정 예시
                    new ThingDeltaData
                    {
                        ThingID = 1001,
                        Type = DeltaType.Moved,
                        Snapshot = new ThingSnapshot
                        {
                            ThingID = 1001,
                            DefName = "Human",
                            X = 50.0f,
                            Y = 0f,
                            Z = 50.0f,
                            HitPointsPercent = 1.0f,
                            IsPawn = true,
                            IsDrafted = true
                        }
                    },
                    // 체력 수정 예시
                    new ThingDeltaData
                    {
                        ThingID = 1002,
                        Type = DeltaType.Damaged,
                        Snapshot = new ThingSnapshot
                        {
                            ThingID = 1002,
                            DefName = "Human",
                            X = 45.0f,
                            Y = 0f,
                            Z = 48.0f,
                            HitPointsPercent = 0.75f,
                            IsPawn = true,
                            IsDrafted = false
                        }
                    }
                },
                OrphanedThingIDs = new List<int> { 9999 }, // 클라이언트에만 있는 Thing (삭제 필요)
                MissingThings = new List<ThingSnapshot>
                {
                    // 클라이언트에 없는 Thing (생성 필요)
                    new ThingSnapshot
                    {
                        ThingID = 2001,
                        DefName = "MealSimple",
                        X = 40.0f,
                        Y = 0f,
                        Z = 40.0f,
                        HitPointsPercent = 1.0f,
                        IsPawn = false
                    }
                }
            };
            
            Console.WriteLine($"[DESYNC-TEST] Sending AuthoritativeStatePacket:");
            Console.WriteLine($"  - Corrections: {resyncPacket.Corrections.Count}");
            Console.WriteLine($"  - OrphanedThingIDs: {resyncPacket.OrphanedThingIDs.Count}");
            Console.WriteLine($"  - MissingThings: {resyncPacket.MissingThings.Count}");
            
            // 모든 참가자에게 전송
            foreach (var clientId in session.ParticipantClientIds)
            {
                if (clients.TryGetValue(clientId, out var info))
                {
                    Console.WriteLine($"[DESYNC-TEST] Sending to client {clientId}");
                    Send(info.Peer, resyncPacket);
                }
            }
            
            Console.WriteLine("[DESYNC-TEST] Delta sync packet sent to all participants");
        }
        
        #endregion

        #region INetEventListener
        
        public void OnPeerConnected(NetPeer peer)
        {
            int id = nextClientId++;
            clients[id] = new ClientInfo { Peer = peer };
            peerToId[peer] = id;
            Console.WriteLine($"[+] Client connected: {peer.EndPoint} (ID: {id}, awaiting auth...)");
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (peerToId.TryGetValue(peer, out int id))
            {
                var info = clients[id];
                string name = info.Username ?? "Unknown";
                
                clients.Remove(id);
                peerToId.Remove(peer);
                
                Console.WriteLine($"[-] Client disconnected: {name} ({disconnectInfo.Reason})");
                
                if (info.Authenticated)
                {
                    BroadcastChat("[SERVER]", $"{name} left the server.", ChatMessageType.System);
                }
            }
        }
        
        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (request.Data.GetString() == PacketConst.ConnectionKey)
            {
                request.Accept();
                Console.WriteLine($"[?] Connection accepted from {request.RemoteEndPoint}");
            }
            else
            {
                request.Reject();
                Console.WriteLine($"[!] Connection rejected from {request.RemoteEndPoint}");
            }
        }
        
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Console.WriteLine($"[!] Network error: {socketError}");
        }
        
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            byte[] data = reader.GetRemainingBytes();
            router.HandlePacket(peer, data);
            reader.Recycle();
        }
        
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        
        #endregion
        
        // ========== InSync 핸들러 ==========
        
        /// <summary>
        /// InSync 세션 관리
        /// </summary>
        private Dictionary<int, InSyncSession> inSyncSessions = new();
        private int nextInSyncSessionId = 1;
        
        private void HandleInSyncRequest(NetPeer peer, InSyncRequestPacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[INSYNC] {clientInfo.Username} requests to enter {packet.TargetUsername}'s settlement at tile {packet.TargetTileId}");
            
            // 대상 유저 찾기
            var targetClient = clients.Values.FirstOrDefault(c => 
                c.Authenticated && c.Username == packet.TargetUsername);
            
            if (targetClient == null || targetClient.Peer == null)
            {
                Console.WriteLine($"[INSYNC] Target {packet.TargetUsername} not online");
                Send(peer, new InSyncResponsePacket { Response = InSyncResponse.Rejected, SessionId = -1 });
                return;
            }
            
            // 세션 생성
            int sessionId = nextInSyncSessionId++;
            var session = new InSyncSession
            {
                SessionId = sessionId,
                AuthorityUsername = packet.TargetUsername,
                InvaderUsername = clientInfo.Username,
                TileId = packet.TargetTileId,
                Mode = packet.Mode,
                AuthorityPeer = targetClient.Peer,
                InvaderPeer = peer
            };
            inSyncSessions[sessionId] = session;
            
            Console.WriteLine($"[INSYNC] Session {sessionId} created");
            
            // 침입자에게 응답
            Send(peer, new InSyncResponsePacket { Response = InSyncResponse.Accepted, SessionId = sessionId });
            
            // 권위자에게 알림 (맵 스냅샷 요청)
            Send(targetClient.Peer, new InSyncNotifyPacket
            {
                RequesterUsername = clientInfo.Username,
                TileId = packet.TargetTileId,
                Mode = packet.Mode,
                SessionId = sessionId
            });
        }
        
        private void HandleMapSnapshot(NetPeer peer, MapSnapshotPacket packet)
        {
            if (!inSyncSessions.TryGetValue(packet.SessionId, out var session))
            {
                Console.WriteLine($"[INSYNC] Invalid session {packet.SessionId}");
                return;
            }
            
            Console.WriteLine($"[INSYNC] Map snapshot received for session {packet.SessionId}, {packet.CompressedMapDataBase64?.Length ?? 0} chars");
            
            // 침입자에게 맵 전달
            if (session.InvaderPeer != null)
            {
                Send(session.InvaderPeer, packet);
            }
        }
        
        private void HandleLockstepCommand(NetPeer peer, LockstepCommandPacket packet)
        {
            if (!inSyncSessions.TryGetValue(packet.SessionId, out var session))
                return;
            
            // 상대방에게 명령 전달
            var targetPeer = (peer == session.AuthorityPeer) ? session.InvaderPeer : session.AuthorityPeer;
            if (targetPeer != null)
            {
                Send(targetPeer, packet);
            }
        }
        
        private void HandleInSyncEnd(NetPeer peer, InSyncEndPacket packet)
        {
            if (!inSyncSessions.TryGetValue(packet.SessionId, out var session))
                return;
            
            Console.WriteLine($"[INSYNC] Session {packet.SessionId} ending: {packet.Reason}");
            
            // 양측에 종료 알림
            if (session.AuthorityPeer != null)
                Send(session.AuthorityPeer, packet);
            if (session.InvaderPeer != null)
                Send(session.InvaderPeer, packet);
            
            inSyncSessions.Remove(packet.SessionId);
        }
    }
    
    /// <summary>
    /// InSync 세션 정보
    /// </summary>
    public class InSyncSession
    {
        public int SessionId { get; set; }
        public string AuthorityUsername { get; set; }
        public string InvaderUsername { get; set; }
        public int TileId { get; set; }
        public InSyncMode Mode { get; set; }
        public NetPeer AuthorityPeer { get; set; }
        public NetPeer InvaderPeer { get; set; }
    }
}
