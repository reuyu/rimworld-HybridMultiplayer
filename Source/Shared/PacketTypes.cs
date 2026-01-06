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
        LoginRequest = 5,
        LoginResponse = 6,
        RegisterRequest = 7,
        RegisterResponse = 8,
        
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
        SettlementCreate = 33,
        SettlementCreateResponse = 34,
        SettlementList = 35,
        SaveUpload = 36,        // 클라이언트 → 서버 세이브 업로드
        SaveDownload = 37,      // 서버 → 클라이언트 세이브 다운로드
        SaveRequest = 38,       // 세이브 요청
        CaravanUpdate = 39,     // 캐러밴 위치 업데이트
        CaravanList = 45,       // 캐러밴 목록
        SettlementRemove = 46,  // 정착지 삭제
        
        // ========== 동기화 (40-49) ==========
        SyncAction = 40,
        SyncField = 41,
        SyncCommand = 42,
        
        // ========== 전투/InSync (50-59) ==========
        BattleStart = 50,
        BattleEnd = 51,
        BattleSync = 52,
        BattleAction = 53,
        BattleReady = 54,
        BattleStateHash = 55,
        InSyncHandover = 56,
        InSyncHandoverComplete = 57,
        InSyncExit = 58,
        
        // ========== Desync/Resync (60-69) ==========
        DesyncDetected = 60,
        FastResync = 61,
        FullResync = 62,
        
        // ========== InSync 전투 (70-79) ==========
        InSyncRequest = 70,
        InSyncNotify = 71,
        InSyncResponse = 72,
        MapSnapshot = 73,
        LockstepTick = 74,
        LockstepCommand = 75,
        InSyncEnd = 76,
        SyncOpinion = 77,  // Desync 감지용
        
        // ========== 관리 (80-89) ==========
        Kick = 80,
        Ban = 81,
        ServerMessage = 82,
        
        // ========== 세력 관계 (90-99) ==========
        FactionRelationsRequest = 90,
        FactionRelationsResponse = 91,
        FactionRelationSync = 92
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
