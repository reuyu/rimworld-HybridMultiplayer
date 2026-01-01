namespace HybridShared
{
    /// <summary>
    /// 패킷 관련 상수
    /// </summary>
    public static class PacketConst
    {
        /// <summary>연결 키 (핸드셰이크)</summary>
        public const string ConnectionKey = "HybridMP_v1";
        
        /// <summary>프로토콜 버전</summary>
        public const int ProtocolVersion = 1;
    }
    
    /// <summary>
    /// 패킷 타입 정의
    /// </summary>
    public enum PacketType : byte
    {
        // ========== 연결 관련 (0-9) ==========
        Handshake = 0,
        HandshakeResponse = 1,
        Disconnect = 2,
        Ping = 3,
        Pong = 4,
        
        // ========== 플레이어 관련 (10-19) ==========
        PlayerList = 10,
        PlayerJoined = 11,
        PlayerLeft = 12,
        
        // ========== 채팅 (20-29) ==========
        Chat = 20,
        
        // ========== 게임 상태 (30-39) ==========
        WorldState = 30,
        MapState = 31,
        PawnState = 32,
        
        // ========== 동기화 (40-49) ==========
        SyncAction = 40,
        SyncField = 41,
        SyncCommand = 42,
        
        // ========== 전투 (50-59) ==========
        BattleStart = 50,
        BattleEnd = 51,
        BattleSync = 52,
        
        // ========== Desync/Resync (60-69) ==========
        DesyncDetected = 60,
        FastResync = 61,
        FullResync = 62,
        
        // ========== 관리 (70-79) ==========
        Kick = 70,
        Ban = 71,
        ServerMessage = 72
    }
    
    /// <summary>
    /// 연결 상태
    /// </summary>
    public enum HybridConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Authenticated,
        InGame,
        InBattle
    }
}
