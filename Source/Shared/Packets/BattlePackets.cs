using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 전투 시작 패킷.
    /// 서버 → 클라이언트: 전투 세션 시작 알림.
    /// </summary>
    public class BattleStartPacket : PacketBase
    {
        public override PacketType Type => PacketType.BattleStart;
        
        /// <summary>고유 전투 세션 ID</summary>
        public string BattleId { get; set; }
        
        /// <summary>참가자 클라이언트 ID 목록</summary>
        public int[] ParticipantIds { get; set; }
        
        /// <summary>난수 동기화 시드</summary>
        public int RandomSeed { get; set; }
        
        /// <summary>시작 틱</summary>
        public int StartTick { get; set; }
        
        /// <summary>맵 데이터 (GZip 압축됨, 옵션)</summary>
        public byte[] MapData { get; set; }
        
        /// <summary>맵 ID</summary>
        public int MapId { get; set; }
    }
    
    /// <summary>
    /// 전투 동기화 패킷.
    /// 서버 → 클라이언트: 매 N틱마다 전송.
    /// </summary>
    public class BattleSyncPacket : PacketBase
    {
        public override PacketType Type => PacketType.BattleSync;
        
        /// <summary>전투 세션 ID</summary>
        public string BattleId { get; set; }
        
        /// <summary>서버 현재 틱</summary>
        public int ServerTick { get; set; }
        
        /// <summary>이번 틱에 실행할 액션 목록</summary>
        public List<ScheduledAction> Actions { get; set; } = new();
        
        /// <summary>서버 상태 해시 (Desync 감지용)</summary>
        public uint ServerStateHash { get; set; }
    }
    
    /// <summary>
    /// 전투 액션 패킷.
    /// 클라이언트 → 서버: 플레이어 입력.
    /// </summary>
    public class BattleActionPacket : PacketBase
    {
        public override PacketType Type => PacketType.BattleAction;
        
        /// <summary>전투 세션 ID</summary>
        public string BattleId { get; set; }
        
        /// <summary>실행할 액션</summary>
        public ScheduledAction Action { get; set; }
    }
    
    /// <summary>
    /// 전투 준비 완료 패킷.
    /// 클라이언트 → 서버: 맵 로딩 완료 알림.
    /// </summary>
    public class BattleReadyPacket : PacketBase
    {
        public override PacketType Type => PacketType.BattleReady;
        
        /// <summary>전투 세션 ID</summary>
        public string BattleId { get; set; }
        
        /// <summary>준비 완료 여부</summary>
        public bool IsReady { get; set; } = true;
    }
    
    /// <summary>
    /// 상태 해시 보고 패킷.
    /// 클라이언트 → 서버: Desync 감지용 상태 해시.
    /// </summary>
    public class BattleStateHashPacket : PacketBase
    {
        public override PacketType Type => PacketType.BattleStateHash;
        
        /// <summary>전투 세션 ID</summary>
        public string BattleId { get; set; }
        
        /// <summary>틱 번호</summary>
        public int Tick { get; set; }
        
        /// <summary>클라이언트 상태 해시</summary>
        public uint StateHash { get; set; }
    }
    
    /// <summary>
    /// 전투 종료 패킷.
    /// 서버 → 클라이언트: 전투 결과 알림.
    /// </summary>
    public class BattleEndPacket : PacketBase
    {
        public override PacketType Type => PacketType.BattleEnd;
        
        /// <summary>전투 세션 ID</summary>
        public string BattleId { get; set; }
        
        /// <summary>전투 결과</summary>
        public BattleResult Result { get; set; }
        
        /// <summary>승자 플레이어 ID (해당하는 경우)</summary>
        public int? WinnerId { get; set; }
        
        /// <summary>전투 지속 시간 (틱)</summary>
        public int DurationTicks { get; set; }
        
        /// <summary>사상자 정보 (ThingID → 상태)</summary>
        public Dictionary<int, CasualtyInfo> Casualties { get; set; } = new();
    }
    
    /// <summary>
    /// 전투 결과
    /// </summary>
    public enum BattleResult : byte
    {
        /// <summary>승리</summary>
        Victory = 0,
        
        /// <summary>패배</summary>
        Defeat = 1,
        
        /// <summary>무승부</summary>
        Draw = 2,
        
        /// <summary>중단됨</summary>
        Aborted = 3
    }
    
    /// <summary>
    /// 사상자 정보
    /// </summary>
    public class CasualtyInfo
    {
        /// <summary>Pawn DefName</summary>
        public string DefName { get; set; }
        
        /// <summary>Pawn 이름</summary>
        public string Name { get; set; }
        
        /// <summary>상태 (Dead, Downed, Captured 등)</summary>
        public CasualtyState State { get; set; }
    }
    
    /// <summary>
    /// 사상자 상태
    /// </summary>
    public enum CasualtyState : byte
    {
        Alive = 0,
        Downed = 1,
        Dead = 2,
        Captured = 3,
        Fled = 4
    }
}
