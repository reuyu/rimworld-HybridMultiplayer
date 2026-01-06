using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using HybridShared;
using HybridShared.Packets;
using HybridClient.Handlers;
using HybridClient.Battle;
using HybridClient.World;
using HybridClient.Save;
using Verse;

namespace HybridClient
{
    /// <summary>
    /// 네트워크 상태
    /// </summary>
    public enum NetworkState
    {
        /// <summary>연결 안됨</summary>
        Disconnected,
        /// <summary>로비 서버 연결 중</summary>
        Connecting,
        /// <summary>로비 서버 연결됨 (비동기 모드)</summary>
        Lobby,
        /// <summary>InSync 서버로 전환 중</summary>
        Transitioning,
        /// <summary>InSync 모드 (실시간 동기화)</summary>
        InSync
    }
    
    /// <summary>
    /// 클라이언트 네트워크 매니저 - 하이브리드 연결 관리
    /// Lobby: UDP (Main Server)
    /// InSync: UDP (Battle/Sync Server)
    /// </summary>
    public class NetworkManager : INetEventListener
    {
        // 싱글톤 인스턴스
        private static NetworkManager _instance;
        public static NetworkManager Instance => _instance ??= new NetworkManager();
        
        private NetManager client;
        private NetPeer serverPeer;
        private ClientPacketRouter router;
        
        // 네트워크 상태
        public NetworkState State { get; private set; } = NetworkState.Disconnected;
        
        // InSync 서버 정보 (전환 시 사용)
        private string _inSyncServerIp;
        private int _inSyncServerPort;
        private string _inSyncSessionId;
        
        // 기존 연결 정보 (복귀 시 사용)
        private string _lobbyServerIp;
        private int _lobbyServerPort;
        
        public bool IsConnected => serverPeer != null && serverPeer.ConnectionState == LiteNetLib.ConnectionState.Connected;
        public bool IsAuthenticated { get; private set; }
        public bool IsInSync => State == NetworkState.InSync;
        
        // 현재 유저명
        public string Username { get; private set; }
        public bool IsInLobby => State == NetworkState.Lobby;
        public string ServerIp { get; private set; }
        public int ServerPort { get; private set; }
        public int SessionId { get; private set; }
        public string ServerName { get; private set; }
        
        // 이벤트
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<HandshakeResponsePacket> OnAuthenticated;
        public event Action<ChatPacket> OnChatReceived;
        public event Action<NetworkState> OnStateChanged;
        public event Action OnInSyncEntered;
        public event Action OnInSyncExited;
        
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
            
            // 전투 패킷 핸들러
            router.Register<BattleStartPacket>(HandleBattleStart);
            router.Register<BattleSyncPacket>(HandleBattleSync);
            router.Register<BattleEndPacket>(HandleBattleEnd);
            router.Register<AuthoritativeStatePacket>(HandleResync);
            
            // InSync 핸드오버 패킷 핸들러
            router.Register<InSyncHandoverPacket>(HandleInSyncHandover);
            router.Register<InSyncExitPacket>(HandleInSyncExit);
            
            // 월드/정착지 패킷 핸들러
            router.Register<WorldPacket>(HandleWorld);
            router.Register<SettlementCreateResponsePacket>(HandleSettlementResponse);
            router.Register<SettlementListPacket>(HandleSettlementList);
            router.Register<SettlementRemovePacket>(HandleSettlementRemove);
            
            // 캐러밴 패킷 핸들러
            router.Register<CaravanListPacket>(HandleCaravanList);
            router.Register<CaravanUpdatePacket>(HandleCaravanUpdate);
            
            // 세이브 패킷 핸들러
            router.Register<SaveDownloadPacket>(HandleSaveDownload);
            
            // InSync Phase 3 패킷 핸들러
            router.Register<InSyncResponsePacket>(HandleInSyncResponse);
            router.Register<InSyncNotifyPacket>(HandleInSyncNotify);
            router.Register<MapSnapshotPacket>(HandleMapSnapshot);
            router.Register<LockstepCommandPacket>(HandleLockstepCommand);
            router.Register<InSyncEndPacket>(HandleInSyncEnd);
            
            // 세력 관계 패킷 핸들러
            router.Register<FactionRelationsResponsePacket>(HandleFactionRelationsResponse);
            router.Register<FactionRelationSyncPacket>(HandleFactionRelationSync);
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
                
                // 접속 성공 시 세력 관계 요청
                FactionRelationSyncManager.RequestAllRelations();
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
        
        #region Battle Packet Handlers
        
        private void HandleBattleStart(BattleStartPacket packet)
        {
            Log.Message($"[HybridMP][BATTLE] Battle start received: {packet.BattleId}");
            BattleController.Instance.Initialize(this, SessionId);
            BattleController.Instance.StartBattle(packet);
        }
        
        private void HandleBattleSync(BattleSyncPacket packet)
        {
            BattleController.Instance.HandleSync(packet);
        }
        
        private void HandleBattleEnd(BattleEndPacket packet)
        {
            Log.Message($"[HybridMP][BATTLE] Battle end received: {packet.Result}");
            BattleController.Instance.EndBattle(packet);
        }
        
        private void HandleResync(AuthoritativeStatePacket packet)
        {
            Log.Message($"[HybridMP][RESYNC] Fast resync received: {packet.Corrections?.Count ?? 0} corrections");
            BattleController.Instance.ApplyResync(packet);
        }
        
        #endregion
        
        #region InSync Handover Handlers
        
        private void HandleInSyncHandover(InSyncHandoverPacket packet)
        {
            Log.Message($"[HybridMP][HANDOVER] InSync handover received: {packet.SessionId}, Reason: {packet.Reason}");
            
            // 핸드오버 처리
            EnterInSync(packet.ServerIp ?? ServerIp, packet.ServerPort, packet.SessionId);
            
            // 전투 컨트롤러 초기화 (전투인 경우)
            if (packet.Reason == HandoverReason.Battle && packet.ParticipantIds != null)
            {
                BattleController.Instance.Initialize(this, SessionId);
            }
            
            // 핸드오버 완료 응답
            Send(new InSyncHandoverCompletePacket
            {
                SessionId = packet.SessionId,
                Success = true
            });
        }
        
        private void HandleInSyncExit(InSyncExitPacket packet)
        {
            Log.Message($"[HybridMP][HANDOVER] InSync exit received: {packet.SessionId}, Reason: {packet.Reason}");
            
            // InSync 종료
            ExitInSync();
            
            // 전투 결과 처리 (전투였던 경우)
            if (packet.BattleResult.HasValue)
            {
                Log.Message($"[HybridMP] Battle result: {packet.BattleResult.Value}");
                // TODO: 전투 결과 UI 표시
            }
        }
        
        #endregion
        
        #region World Handlers
        
        private void HandleWorld(WorldPacket packet)
        {
            Log.Message($"[HybridMP][WORLD] World packet received: {packet.StepMode}");
            ClientWorldManager.Instance.HandleWorldPacket(packet);
        }
        
        private void HandleSettlementResponse(SettlementCreateResponsePacket packet)
        {
            Log.Message($"[HybridMP][WORLD] Settlement response: Success={packet.Success}");
            ClientWorldManager.Instance.HandleSettlementResponse(packet);
        }
        
        private void HandleSettlementList(SettlementListPacket packet)
        {
            Log.Message($"[HybridMP][WORLD] Settlement list: {packet.Settlements?.Count ?? 0} settlements");
            ClientWorldManager.Instance.HandleSettlementList(packet);
        }
        
        private void HandleSettlementRemove(SettlementRemovePacket packet)
        {
            Log.Message($"[HybridMP][WORLD] Settlement removed at tile {packet.TileId}");
            
            // 월드에서 해당 타일의 정착지 찾아서 삭제
            var settlement = Find.WorldObjects.SettlementAt(packet.TileId);
            if (settlement != null && settlement.Faction != RimWorld.Faction.OfPlayer)
            {
                Log.Message($"[HybridMP][WORLD] Removing settlement: {settlement.Name} at tile {packet.TileId}");
                Find.WorldObjects.Remove(settlement);
            }
            
            // 메모리에서도 삭제
            ClientWorldManager.Instance.CurrentWorld?.PlayerSettlements?.RemoveAll(s => s.TileId == packet.TileId);
        }
        
        private void HandleSaveDownload(SaveDownloadPacket packet)
        {
            ClientSaveManager.Instance.HandleSaveDownload(packet);
        }
        
        private void HandleCaravanList(CaravanListPacket packet)
        {
            Log.Message($"[HybridMP][CARAVAN] Caravan list received: {packet.Caravans?.Count ?? 0} caravans");
            CaravanSync.ClientCaravanManager.Instance.HandleCaravanList(packet);
            
            // 월드에 캐러밴 표시
            if (Find.World != null)
            {
                World.ClientWorldManager.Instance?.SyncCaravansAfterLoad();
            }
        }
        
        private void HandleCaravanUpdate(CaravanUpdatePacket packet)
        {
            Log.Message($"[HybridMP][CARAVAN] Caravan update: {packet.StepMode} from {packet.Caravan?.OwnerUsername}");
            CaravanSync.ClientCaravanManager.Instance.HandleCaravanUpdate(packet);
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
            
            SetState(NetworkState.Connecting);
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
            SetState(NetworkState.Disconnected);
        }
        
        #region InSync Mode Transition
        
        /// <summary>
        /// 상태 변경 (내부용)
        /// </summary>
        private void SetState(NetworkState newState)
        {
            if (State == newState) return;
            
            var oldState = State;
            State = newState;
            
            Log.Message($"[HybridMP] State: {oldState} -> {newState}");
            OnStateChanged?.Invoke(newState);
        }
        
        /// <summary>
        /// InSync 모드 진입 (서버에서 InSyncStartPacket 수신 시 호출)
        /// </summary>
        /// <param name="serverIp">InSync 서버 IP</param>
        /// <param name="serverPort">InSync 서버 포트</param>
        /// <param name="sessionId">InSync 세션 ID</param>
        public void EnterInSync(string serverIp, int serverPort, string sessionId)
        {
            if (State != NetworkState.Lobby)
            {
                Log.Warning($"[HybridMP] Cannot enter InSync: not in Lobby state (current: {State})");
                return;
            }
            
            Log.Message($"[HybridMP] Entering InSync mode: {serverIp}:{serverPort}");
            
            // 현재 로비 서버 정보 저장 (나중에 복귀용)
            _lobbyServerIp = ServerIp;
            _lobbyServerPort = ServerPort;
            
            // InSync 서버 정보 저장
            _inSyncServerIp = serverIp;
            _inSyncServerPort = serverPort;
            _inSyncSessionId = sessionId;
            
            // 상태 전환
            SetState(NetworkState.Transitioning);
            
            // 현재 연결 유지한 채 InSync 준비
            // (실제로는 같은 서버의 다른 모드일 수 있음)
            // TODO: 다른 서버로 전환이 필요한 경우 재연결 로직 추가
            
            SetState(NetworkState.InSync);
            OnInSyncEntered?.Invoke();
            
            Log.Message($"[HybridMP] InSync mode entered (Session: {sessionId})");
        }
        
        /// <summary>
        /// InSync 모드 종료 → 로비로 복귀
        /// </summary>
        public void ExitInSync()
        {
            if (State != NetworkState.InSync)
            {
                Log.Warning($"[HybridMP] Cannot exit InSync: not in InSync state (current: {State})");
                return;
            }
            
            Log.Message("[HybridMP] Exiting InSync mode...");
            
            SetState(NetworkState.Transitioning);
            
            // TODO: 다른 서버였다면 로비 서버로 재연결
            // 현재는 같은 서버이므로 상태만 변경
            
            SetState(NetworkState.Lobby);
            OnInSyncExited?.Invoke();
            
            Log.Message("[HybridMP] Returned to Lobby mode");
        }
        
        /// <summary>
        /// InSync 친구 확인 (같은 맵에 있는지)
        /// </summary>
        public bool CanInteractWith(int otherPlayerId)
        {
            // TODO: 서버에서 받은 정보로 확인
            return IsInSync;
        }
        
        #endregion
        
        #region INetEventListener
        
        public void OnPeerConnected(NetPeer peer)
        {
            serverPeer = peer;
            Log.Message($"[HybridMP] Connected to server! Ping: {peer.Ping}ms");
            
            // 상태를 Lobby로 변경 (인증 전이지만 연결됨)
            SetState(NetworkState.Lobby);
            
            // 유저명 저장
            Username = _pendingUsername ?? "Player";
            
            // 핸드셰이크 전송
            var handshake = new HandshakePacket(Username);
            Send(handshake);
            
            OnConnected?.Invoke();
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            serverPeer = null;
            IsAuthenticated = false;
            SetState(NetworkState.Disconnected);
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
        
        #region InSync Handlers
        
        private void HandleInSyncResponse(InSyncResponsePacket packet)
        {
            InSync.InSyncManager.Instance.HandleInSyncResponse(packet);
        }
        
        private void HandleInSyncNotify(InSyncNotifyPacket packet)
        {
            InSync.InSyncManager.Instance.HandleInSyncNotify(packet);
        }
        
        private void HandleMapSnapshot(MapSnapshotPacket packet)
        {
            InSync.InSyncManager.Instance.HandleMapSnapshot(packet);
        }
        
        private void HandleLockstepCommand(LockstepCommandPacket packet)
        {
            InSync.InSyncManager.Instance.HandleLockstepCommand(packet);
        }
        
        private void HandleInSyncEnd(InSyncEndPacket packet)
        {
            InSync.InSyncManager.Instance.HandleInSyncEnd(packet);
        }
        
        private void HandleFactionRelationsResponse(FactionRelationsResponsePacket packet)
        {
            FactionRelationSyncManager.ApplyRelations(packet.Relations);
        }
        
        private void HandleFactionRelationSync(FactionRelationSyncPacket packet)
        {
            FactionRelationSyncManager.OnRelationChanged(packet);
        }
        
        #endregion
    }
}
