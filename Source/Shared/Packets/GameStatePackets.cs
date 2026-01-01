using System;
using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 폰 상태 패킷 - 실시간 동기화용
    /// </summary>
    public class PawnStatePacket : PacketBase
    {
        public override PacketType Type => PacketType.PawnState;
        
        /// <summary>폰 ID (ThingID)</summary>
        public int ThingID { get; set; }
        
        /// <summary>위치 (x, y, z)</summary>
        public float[] Position { get; set; } = new float[3];
        
        /// <summary>체력 (0.0 ~ 1.0)</summary>
        public float HealthPercent { get; set; }
        
        /// <summary>현재 작업 DefName</summary>
        public string CurrentJobDefName { get; set; }
        
        /// <summary>징집 상태</summary>
        public bool IsDrafted { get; set; }
        
        /// <summary>팩션 ID</summary>
        public int FactionId { get; set; }
        
        /// <summary>폰 DefName</summary>
        public string DefName { get; set; }
        
        public PawnStatePacket() { }
        
        public PawnStatePacket(int thingId, float x, float y, float z, float health)
        {
            ThingID = thingId;
            Position = new[] { x, y, z };
            HealthPercent = health;
        }
    }
    
    /// <summary>
    /// 다중 폰 상태 패킷 - 일괄 동기화용
    /// </summary>
    public class PawnStateBatchPacket : PacketBase
    {
        public override PacketType Type => PacketType.PawnState;
        
        /// <summary>타임스탬프 (틱)</summary>
        public int Tick { get; set; }
        
        /// <summary>폰 상태 목록</summary>
        public List<PawnStateData> Pawns { get; set; } = new();
    }
    
    /// <summary>
    /// 폰 상태 데이터 (경량화)
    /// </summary>
    public class PawnStateData
    {
        public int ThingID { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Health { get; set; }
        public string JobDef { get; set; }
        public bool Drafted { get; set; }
    }
    
    /// <summary>
    /// 맵 상태 패킷 - 맵 데이터 전송용
    /// </summary>
    public class MapStatePacket : PacketBase
    {
        public override PacketType Type => PacketType.MapState;
        
        /// <summary>맵 ID</summary>
        public int MapId { get; set; }
        
        /// <summary>맵 데이터 (GZip 압축됨)</summary>
        public byte[] MapData { get; set; }
        
        /// <summary>총 청크 수 (분할 전송 시)</summary>
        public int TotalChunks { get; set; } = 1;
        
        /// <summary>현재 청크 번호</summary>
        public int ChunkIndex { get; set; } = 0;
        
        /// <summary>체크섬 (무결성 확인용)</summary>
        public uint Checksum { get; set; }
    }
    
    /// <summary>
    /// 월드 상태 패킷 - Fast Resync용
    /// </summary>
    public class WorldStatePacket : PacketBase
    {
        public override PacketType Type => PacketType.WorldState;
        
        /// <summary>서버 틱</summary>
        public int ServerTick { get; set; }
        
        /// <summary>폰 상태 목록</summary>
        public List<PawnStateData> Pawns { get; set; } = new();
        
        /// <summary>건물 상태 목록</summary>
        public List<BuildingStateData> Buildings { get; set; } = new();
    }
    
    /// <summary>
    /// 건물 상태 데이터
    /// </summary>
    public class BuildingStateData
    {
        public int ThingID { get; set; }
        public string DefName { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float HitPointsPercent { get; set; }
        public bool Destroyed { get; set; }
    }
}
