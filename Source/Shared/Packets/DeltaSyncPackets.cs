using System;
using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 델타 변경 타입
    /// </summary>
    public enum DeltaType : byte
    {
        /// <summary>Thing 생성됨</summary>
        Created = 0,
        /// <summary>Thing 파괴됨</summary>
        Destroyed = 1,
        /// <summary>위치 변경</summary>
        Moved = 2,
        /// <summary>체력 변경</summary>
        Damaged = 3,
        /// <summary>상태 변경 (작업, 징집 등)</summary>
        StateChanged = 4,
        /// <summary>인벤토리 변경</summary>
        InventoryChanged = 5,
        /// <summary>장비 변경</summary>
        EquipmentChanged = 6
    }
    
    /// <summary>
    /// Thing 스냅샷 - 현재 상태 캡처
    /// </summary>
    public class ThingSnapshot
    {
        public int ThingID { get; set; }
        public string DefName { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float HitPointsPercent { get; set; }
        public int FactionId { get; set; }
        
        // Pawn 전용
        public bool IsPawn { get; set; }
        public string CurrentJobDef { get; set; }
        public bool IsDrafted { get; set; }
        public bool IsDowned { get; set; }
        public bool IsDead { get; set; }
        
        public override int GetHashCode()
        {
            // 비교용 해시 (위치 + 체력 + 상태)
            int hash = ThingID;
            hash = hash * 31 + (int)(X * 10);
            hash = hash * 31 + (int)(Z * 10);
            hash = hash * 31 + (int)(HitPointsPercent * 100);
            if (IsPawn)
            {
                hash = hash * 31 + (IsDrafted ? 1 : 0);
                hash = hash * 31 + (CurrentJobDef?.GetHashCode() ?? 0);
            }
            return hash;
        }
        
        public override bool Equals(object obj)
        {
            if (obj is ThingSnapshot other)
            {
                return ThingID == other.ThingID &&
                       Math.Abs(X - other.X) < 0.1f &&
                       Math.Abs(Z - other.Z) < 0.1f &&
                       Math.Abs(HitPointsPercent - other.HitPointsPercent) < 0.01f &&
                       IsDrafted == other.IsDrafted &&
                       CurrentJobDef == other.CurrentJobDef;
            }
            return false;
        }
    }
    
    /// <summary>
    /// 델타 동기화 패킷 - 개별 Thing 변경
    /// </summary>
    public class ThingDeltaPacket : PacketBase
    {
        public override PacketType Type => PacketType.SyncField;
        
        public int ThingID { get; set; }
        public DeltaType DeltaType { get; set; }
        public ThingSnapshot Snapshot { get; set; }
    }
    
    /// <summary>
    /// 일괄 델타 동기화 패킷
    /// </summary>
    public class DeltaBatchPacket : PacketBase
    {
        public override PacketType Type => PacketType.SyncAction;
        
        /// <summary>서버 틱</summary>
        public int ServerTick { get; set; }
        
        /// <summary>변경된 Thing 목록</summary>
        public List<ThingDeltaData> Deltas { get; set; } = new();
    }
    
    /// <summary>
    /// 델타 데이터 (경량화)
    /// </summary>
    public class ThingDeltaData
    {
        public int ThingID { get; set; }
        public DeltaType Type { get; set; }
        public ThingSnapshot Snapshot { get; set; }
    }
    
    /// <summary>
    /// 클라이언트 상태 보고 패킷
    /// </summary>
    public class ClientStatePacket : PacketBase
    {
        public override PacketType Type => PacketType.SyncCommand;
        
        /// <summary>클라이언트 틱</summary>
        public int ClientTick { get; set; }
        
        /// <summary>Thing 스냅샷 목록</summary>
        public List<ThingSnapshot> Things { get; set; } = new();
        
        /// <summary>전체 상태 해시 (빠른 비교용)</summary>
        public int StateHash { get; set; }
    }
    
    /// <summary>
    /// 서버의 권위 있는 상태 응답
    /// </summary>
    public class AuthoritativeStatePacket : PacketBase
    {
        public override PacketType Type => PacketType.FastResync;
        
        /// <summary>서버 틱</summary>
        public int ServerTick { get; set; }
        
        /// <summary>수정이 필요한 Thing 목록</summary>
        public List<ThingDeltaData> Corrections { get; set; } = new();
        
        /// <summary>클라이언트에만 있는 Thing (삭제 필요)</summary>
        public List<int> OrphanedThingIDs { get; set; } = new();
        
        /// <summary>클라이언트에 없는 Thing (생성 필요)</summary>
        public List<ThingSnapshot> MissingThings { get; set; } = new();
    }
}
