using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using HybridShared;
using HybridShared.Packets;
using HybridClient.Handlers;
using Verse;

namespace HybridClient
{
    /// <summary>
    /// 클라이언트 네트워크 매니저 - UDP 연결 관리
    /// </summary>
    public class NetworkManager : INetEventListener
    {
        private NetManager client;
        private NetPeer serverPeer;
        private ClientPacketRouter router;
        
        public bool IsConnected => serverPeer != null && serverPeer.ConnectionState == LiteNetLib.ConnectionState.Connected;
        public bool IsAuthenticated { get; private set; }
        public string ServerIp { get; private set; }
        public int ServerPort { get; private set; }
        public int SessionId { get; private set; }
        public string ServerName { get; private set; }
        
        // 이벤트
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<HandshakeResponsePacket> OnAuthenticated;
        public event Action<ChatPacket> OnChatReceived;
        
        public NetworkManager()
        {
            client = new NetManager(this);
            router = new ClientPacketRouter();
            RegisterPacketHandlers();
            Log.Message("[HybridMP] NetworkManager initialized");
        }
        
        private void RegisterPacketHandlers()
        {
            router.Register<HandshakeResponsePacket>(HandleHandshakeResponse);
            router.Register<PongPacket>(HandlePong);
            router.Register<ChatPacket>(HandleChat);
            router.Register<PlayerListPacket>(HandlePlayerList);
        }
        
        #region Packet Handlers
        
        private void HandleHandshakeResponse(HandshakeResponsePacket packet)
        {
            if (packet.Success)
            {
                IsAuthenticated = true;
                SessionId = packet.SessionId;
                ServerName = packet.ServerName;
                Log.Message($"[HybridMP] Authenticated! SessionId: {SessionId}, Server: {ServerName}");
                OnAuthenticated?.Invoke(packet);
            }
            else
            {
                Log.Warning($"[HybridMP] Authentication failed: {packet.Message}");
            }
        }
        
        private void HandlePong(PongPacket packet)
        {
            long rtt = DateTime.UtcNow.Ticks - packet.OriginalTimestamp;
            int pingMs = (int)(rtt / TimeSpan.TicksPerMillisecond);
            Log.Message($"[HybridMP] Pong received, RTT: {pingMs}ms");
        }
        
        private void HandleChat(ChatPacket packet)
        {
            Log.Message($"[HybridMP][CHAT] {packet.SenderName}: {packet.Message}");
            OnChatReceived?.Invoke(packet);
        }
        
        private void HandlePlayerList(PlayerListPacket packet)
        {
            Log.Message($"[HybridMP] Player list: {packet.Players.Count} players");
            foreach (var player in packet.Players)
            {
                Log.Message($"  - {player.Username} (Ping: {player.Ping}ms)");
            }
        }
        
        #endregion
        
        /// <summary>
        /// 서버에 연결
        /// </summary>
        public void Connect(string ip, int port, string username = "Player")
        {
            if (IsConnected)
            {
                Log.Warning("[HybridMP] Already connected!");
                return;
            }
            
            ServerIp = ip;
            ServerPort = port;
            
            if (!client.IsRunning)
            {
                client.Start();
            }
            
            Log.Message($"[HybridMP] Connecting to {ip}:{port}...");
            client.Connect(ip, port, PacketConst.ConnectionKey);
            
            // 연결 후 핸드셰이크는 OnPeerConnected에서 처리
            _pendingUsername = username;
        }
        
        private string _pendingUsername;
        
        /// <summary>
        /// 서버 연결 해제
        /// </summary>
        public void Disconnect()
        {
            if (serverPeer != null)
            {
                serverPeer.Disconnect();
                serverPeer = null;
            }
            IsAuthenticated = false;
            Log.Message("[HybridMP] Disconnected");
        }
        
        /// <summary>
        /// 매 프레임 호출 - 네트워크 이벤트 처리
        /// </summary>
        public void Update()
        {
            if (client != null && client.IsRunning)
            {
                client.PollEvents();
            }
        }
        
        /// <summary>
        /// 서버로 패킷 전송
        /// </summary>
        public void Send(PacketBase packet, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
        {
            if (IsConnected)
            {
                serverPeer.Send(packet.Serialize(), method);
            }
        }
        
        /// <summary>
        /// 채팅 메시지 전송
        /// </summary>
        public void SendChat(string message)
        {
            if (!IsAuthenticated)
            {
                Log.Warning("[HybridMP] Cannot send chat: not authenticated");
                return;
            }
            Send(new ChatPacket(message));
        }
        
        /// <summary>
        /// Ping 전송
        /// </summary>
        public void SendPing()
        {
            if (IsConnected)
            {
                Send(new PingPacket());
            }
        }
        
        /// <summary>
        /// 종료
        /// </summary>
        public void Shutdown()
        {
            Disconnect();
            if (client != null && client.IsRunning)
            {
                client.Stop();
            }
        }

        #region INetEventListener
        
        public void OnPeerConnected(NetPeer peer)
        {
            serverPeer = peer;
            Log.Message($"[HybridMP] Connected to server! Ping: {peer.Ping}ms");
            
            // 핸드셰이크 전송
            var handshake = new HandshakePacket(_pendingUsername ?? "Player");
            Send(handshake);
            
            OnConnected?.Invoke();
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            serverPeer = null;
            IsAuthenticated = false;
            Log.Message($"[HybridMP] Disconnected: {disconnectInfo.Reason}");
            OnDisconnected?.Invoke(disconnectInfo.Reason.ToString());
        }
        
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Log.Error($"[HybridMP] Network error: {socketError}");
        }
        
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            byte[] data = reader.GetRemainingBytes();
            router.HandlePacket(data);
            reader.Recycle();
        }
        
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnConnectionRequest(ConnectionRequest request) { request.Reject(); }
        
        #endregion
    }
}
