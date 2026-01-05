using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 캐러밴 관련 패킷
    /// </summary>
    
    public enum CaravanStepMode
    {
        Add,
        Remove,
        Move
    }
    
    /// <summary>
    /// 캐러밴 정보
    /// </summary>
    public class CaravanInfo
    {
        public int Tile { get; set; }
        public string OwnerUsername { get; set; }
        public int CaravanId { get; set; }
    }
    
    /// <summary>
    /// 캐러밴 업데이트 패킷 (클라이언트 → 서버)
    /// </summary>
    public class CaravanUpdatePacket : PacketBase
    {
        public override PacketType Type => PacketType.CaravanUpdate;
        
        public CaravanStepMode StepMode { get; set; }
        public CaravanInfo Caravan { get; set; }
    }
    
    /// <summary>
    /// 캐러밴 목록 패킷 (서버 → 클라이언트)
    /// </summary>
    public class CaravanListPacket : PacketBase
    {
        public override PacketType Type => PacketType.CaravanList;
        
        public List<CaravanInfo> Caravans { get; set; } = new List<CaravanInfo>();
    }
}
