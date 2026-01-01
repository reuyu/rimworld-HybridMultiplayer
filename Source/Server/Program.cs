using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using HybridShared;
using HybridShared.Packets;
using HybridServer.Handlers;

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
            Console.WriteLine("Commands: quit, list, kick <id>, say <message>");
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
                    
                default:
                    Console.WriteLine($"Unknown command: {cmd}");
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
        
        public int ClientCount => clients.Count;
        public string ServerName { get; set; } = "Hybrid MP Server";
        
        public ServerNetworkManager(int port)
        {
            this.port = port;
            server = new NetManager(this);
            
            // 패킷 핸들러 등록
            router = new ServerPacketRouter();
            RegisterPacketHandlers();
        }
        
        private void RegisterPacketHandlers()
        {
            router.Register<HandshakePacket>(HandleHandshake);
            router.Register<PingPacket>(HandlePing);
            router.Register<ChatPacket>(HandleChat);
            router.Register<PawnStatePacket>(HandlePawnState);
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
        
        private void HandlePawnState(NetPeer peer, PawnStatePacket packet)
        {
            int clientId = peerToId[peer];
            var clientInfo = clients[clientId];
            
            if (!clientInfo.Authenticated) return;
            
            Console.WriteLine($"[PAWN] {clientInfo.Username}: ThingID={packet.ThingID}, " +
                            $"Pos=({packet.Position[0]:F1},{packet.Position[1]:F1},{packet.Position[2]:F1}), " +
                            $"HP={packet.HealthPercent:P0}, Job={packet.CurrentJobDefName}, Drafted={packet.IsDrafted}");
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

        #region INetEventListener
        
        public void OnPeerConnected(NetPeer peer)
        {
            int id = nextClientId++;
            clients[id] = new ClientInfo { Peer = peer };
            peerToId[peer] = id;
            Console.WriteLine($"[+] Client connected: {peer.EndPoint} (ID: {id})");
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
    }
}
