namespace HybridShared.Packets
{
    /// <summary>
    /// InSync 핸드오버 요청 패킷.
    /// 서버 → 클라이언트: Lobby에서 InSync 모드로 전환 요청.
    /// </summary>
    public class InSyncHandoverPacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncHandover;
        
        /// <summary>InSync 세션 ID</summary>
        public string SessionId { get; set; }
        
        /// <summary>InSync 서버 IP (다른 서버인 경우)</summary>
        public string ServerIp { get; set; }
        
        /// <summary>InSync 서버 포트</summary>
        public int ServerPort { get; set; }
        
        /// <summary>인증 토큰</summary>
        public string AuthToken { get; set; }
        
        /// <summary>핸드오버 사유</summary>
        public HandoverReason Reason { get; set; }
        
        /// <summary>관련 플레이어 ID들</summary>
        public int[] ParticipantIds { get; set; }
        
        /// <summary>타겟 맵 ID (해당하는 경우)</summary>
        public int? TargetMapId { get; set; }
    }
    
    /// <summary>
    /// InSync 핸드오버 완료 패킷.
    /// 클라이언트 → 서버: InSync 모드 전환 완료 알림.
    /// </summary>
    public class InSyncHandoverCompletePacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncHandoverComplete;
        
        /// <summary>InSync 세션 ID</summary>
        public string SessionId { get; set; }
        
        /// <summary>성공 여부</summary>
        public bool Success { get; set; }
        
        /// <summary>실패 시 사유</summary>
        public string FailureReason { get; set; }
    }
    
    /// <summary>
    /// InSync 모드 종료 패킷.
    /// 서버 → 클라이언트: InSync 모드 종료 및 Lobby 복귀.
    /// </summary>
    public class InSyncExitPacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncExit;
        
        /// <summary>InSync 세션 ID</summary>
        public string SessionId { get; set; }
        
        /// <summary>종료 사유</summary>
        public InSyncExitReason Reason { get; set; }
        
        /// <summary>결과 요약 (전투인 경우)</summary>
        public BattleResult? BattleResult { get; set; }
    }
    
    /// <summary>
    /// 핸드오버 사유
    /// </summary>
    public enum HandoverReason : byte
    {
        /// <summary>전투 (선전포고 후 침략)</summary>
        Battle = 0,
        
        /// <summary>같은 정착지에서 공동 생활</summary>
        CoLiving = 1,
        
        /// <summary>같은 타일에서 상단 상호작용</summary>
        CaravanMeeting = 2,
        
        /// <summary>방문 (평화적)</summary>
        Visit = 3
    }
    
    /// <summary>
    /// InSync 종료 사유
    /// </summary>
    public enum InSyncExitReason : byte
    {
        /// <summary>전투 종료</summary>
        BattleEnded = 0,
        
        /// <summary>상단 이탈 (다른 타일로 이동)</summary>
        CaravanLeft = 1,
        
        /// <summary>플레이어 연결 해제</summary>
        PlayerDisconnected = 2,
        
        /// <summary>수동 종료 요청</summary>
        ManualExit = 3,
        
        /// <summary>서버 종료</summary>
        ServerShutdown = 4,
        
        /// <summary>타임아웃</summary>
        Timeout = 5
    }
}
