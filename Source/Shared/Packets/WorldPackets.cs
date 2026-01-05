using System;
using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 월드 관련 패킷 - RT 방식 기반
    /// 월드 시드, NPC 세력, 정착지 정보 동기화
    /// </summary>
    
    /// <summary>
    /// 월드 요청/응답 패킷
    /// </summary>
    public class WorldPacket : PacketBase
    {
        public override PacketType Type => PacketType.WorldState;
        
        /// <summary>월드 스텝 모드</summary>
        public WorldStepMode StepMode { get; set; }
        
        /// <summary>월드 데이터 (직렬화된 PlanetConfig)</summary>
        public byte[] WorldData { get; set; }
    }
    
    /// <summary>
    /// 월드 스텝 모드
    /// </summary>
    public enum WorldStepMode : byte
    {
        /// <summary>서버가 클라이언트에게 월드 생성 요청</summary>
        RequestCreate = 0,
        /// <summary>클라이언트가 생성한 월드를 서버에 전송</summary>
        SendToServer = 1,
        /// <summary>서버가 기존 월드를 클라이언트에 전송</summary>
        SendToClient = 2
    }
    
    /// <summary>
    /// 행성 설정 데이터 - RT PlanetConfigFile 기반
    /// </summary>
    public class PlanetConfig
    {
        /// <summary>월드 시드 문자열</summary>
        public string SeedString { get; set; }
        
        /// <summary>영구 랜덤 값 (시드 해시)</summary>
        public int PersistentRandomValue { get; set; }
        
        /// <summary>행성 커버리지 (0.05 ~ 1.0)</summary>
        public float PlanetCoverage { get; set; } = 0.3f;
        
        /// <summary>강수량 (0=극도건조 ~ 6=극도습함)</summary>
        public int Rainfall { get; set; } = 3;
        
        /// <summary>온도 (0=극도한랭 ~ 6=극도온난)</summary>
        public int Temperature { get; set; } = 3;
        
        /// <summary>인구 (0=없음 ~ 6=매우많음)</summary>
        public int Population { get; set; } = 3;
        
        /// <summary>오염도</summary>
        public float Pollution { get; set; } = 0f;
        
        // ========== 게임 파라미터 (첫 유저가 설정) ==========
        
        /// <summary>시나리오 DefName</summary>
        public string ScenarioDefName { get; set; }
        
        /// <summary>이야기꾼 DefName</summary>
        public string StorytellerDefName { get; set; }
        
        /// <summary>난이도 DefName</summary>
        public string DifficultyDefName { get; set; }
        
        // ========== 세력/정착지 ==========
        
        /// <summary>NPC 세력 목록</summary>
        public List<NPCFactionInfo> NPCFactions { get; set; } = new();
        
        /// <summary>NPC 정착지 목록</summary>
        public List<NPCSettlementInfo> NPCSettlements { get; set; } = new();
        
        /// <summary>플레이어 정착지 목록</summary>
        public List<PlayerSettlementInfo> PlayerSettlements { get; set; } = new();
        
        /// <summary>플레이어 캐러밴 목록</summary>
        public List<CaravanInfo> PlayerCaravans { get; set; } = new();
        
        // ========== 도로/야영지 ==========
        
        /// <summary>월드 도로 목록</summary>
        public List<RoadDetail> Roads { get; set; } = new();
        
        /// <summary>야영지/사이트 목록</summary>
        public List<SiteInfo> Sites { get; set; } = new();
    }
    
    /// <summary>
    /// NPC 세력 정보
    /// </summary>
    public class NPCFactionInfo
    {
        public string DefName { get; set; }
        public string Name { get; set; }
        public float[] Color { get; set; } = new float[4];
    }
    
    /// <summary>
    /// NPC 정착지 정보
    /// </summary>
    public class NPCSettlementInfo
    {
        public int TileId { get; set; }
        public string DefName { get; set; }
        public string Name { get; set; }
        public string FactionName { get; set; }
    }
    
    /// <summary>
    /// 플레이어 정착지 정보
    /// </summary>
    public class PlayerSettlementInfo
    {
        public int TileId { get; set; }
        public string SettlementName { get; set; }
        public string OwnerUsername { get; set; }
        public int OwnerId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
    /// <summary>
    /// 정착지 생성 요청
    /// </summary>
    public class SettlementCreatePacket : PacketBase
    {
        public override PacketType Type => PacketType.SettlementCreate;
        
        public int TileId { get; set; }
        public string SettlementName { get; set; }
    }
    
    /// <summary>
    /// 정착지 생성 응답
    /// </summary>
    public class SettlementCreateResponsePacket : PacketBase
    {
        public override PacketType Type => PacketType.SettlementCreateResponse;
        
        public bool Success { get; set; }
        public string Message { get; set; }
        public PlayerSettlementInfo Settlement { get; set; }
    }
    
    /// <summary>
    /// 정착지 목록 업데이트 (브로드캐스트)
    /// </summary>
    public class SettlementListPacket : PacketBase
    {
        public override PacketType Type => PacketType.SettlementList;
        
        public List<PlayerSettlementInfo> Settlements { get; set; } = new();
    }
    
    /// <summary>
    /// 정착지 삭제 요청 (클라이언트 → 서버)
    /// </summary>
    public class SettlementRemovePacket : PacketBase
    {
        public override PacketType Type => PacketType.SettlementRemove;
        
        public int TileId { get; set; }
    }
    
    /// <summary>
    /// 도로 정보 (RT RoadDetail 패턴)
    /// </summary>
    public class RoadDetail
    {
        public int FromTile { get; set; }
        public int ToTile { get; set; }
        public string DefName { get; set; }
    }
    
    /// <summary>
    /// 야영지/사이트 정보
    /// </summary>
    public class SiteInfo
    {
        public int TileId { get; set; }
        public string DefName { get; set; }
        public string FactionDefName { get; set; }
    }
}

