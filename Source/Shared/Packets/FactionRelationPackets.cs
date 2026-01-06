using System;
using System.Collections.Generic;
using HybridShared;

namespace HybridShared.Packets
{
    // ========== 세력 관계 데이터 ==========
    
    /// <summary>
    /// 세력 관계 종류
    /// </summary>
    public enum FactionRelationKindNetwork : byte
    {
        Hostile = 0,
        Neutral = 1,
        Ally = 2
    }
    
    /// <summary>
    /// 세력 관계 데이터 (서버 저장/전송용)
    /// </summary>
    public class FactionRelationData
    {
        /// <summary>세력 A 식별자 (유저명 또는 AI DefName)</summary>
        public string FactionA { get; set; }
        
        /// <summary>세력 B 식별자 (유저명 또는 AI DefName)</summary>
        public string FactionB { get; set; }
        
        /// <summary>관계 종류</summary>
        public FactionRelationKindNetwork Kind { get; set; }
        
        /// <summary>우호도 (-100 ~ 100)</summary>
        public int Goodwill { get; set; }
    }
    
    // ========== 세력 관계 패킷 ==========
    
    /// <summary>
    /// 전체 세력 관계 요청 (클라이언트 → 서버)
    /// 접속 시 서버에서 모든 관계 데이터를 받기 위해 사용
    /// </summary>
    public class FactionRelationsRequestPacket : PacketBase
    {
        public override PacketType Type => PacketType.FactionRelationsRequest;
        
        /// <summary>요청자 유저명</summary>
        public string Username { get; set; }
    }
    
    /// <summary>
    /// 전체 세력 관계 응답 (서버 → 클라이언트)
    /// </summary>
    public class FactionRelationsResponsePacket : PacketBase
    {
        public override PacketType Type => PacketType.FactionRelationsResponse;
        
        /// <summary>모든 세력 관계 목록</summary>
        public List<FactionRelationData> Relations { get; set; } = new List<FactionRelationData>();
    }
    
    /// <summary>
    /// 세력 관계 변경 동기화 (양방향)
    /// - 클라이언트 → 서버: 관계 변경 알림
    /// - 서버 → 클라이언트: 변경 사항 브로드캐스트
    /// </summary>
    public class FactionRelationSyncPacket : PacketBase
    {
        public override PacketType Type => PacketType.FactionRelationSync;
        
        /// <summary>세력 A 식별자</summary>
        public string FactionA { get; set; }
        
        /// <summary>세력 B 식별자</summary>
        public string FactionB { get; set; }
        
        /// <summary>새 관계 종류</summary>
        public FactionRelationKindNetwork NewKind { get; set; }
        
        /// <summary>새 우호도</summary>
        public int NewGoodwill { get; set; }
        
        /// <summary>변경 사유 (로그용)</summary>
        public string Reason { get; set; }
    }
}
