using System;

namespace HybridShared.Packets
{
    /// <summary>
    /// 연결 유지용 Ping
    /// </summary>
    public class PingPacket : PacketBase
    {
        public override PacketType Type => PacketType.Ping;
        
        /// <summary>전송 시간 (Tick)</summary>
        public long Timestamp { get; set; }
        
        public PingPacket()
        {
            Timestamp = DateTime.UtcNow.Ticks;
        }
    }
    
    /// <summary>
    /// Ping 응답
    /// </summary>
    public class PongPacket : PacketBase
    {
        public override PacketType Type => PacketType.Pong;
        
        /// <summary>원본 Ping의 Timestamp</summary>
        public long OriginalTimestamp { get; set; }
        
        /// <summary>서버 시간</summary>
        public long ServerTimestamp { get; set; }
        
        public PongPacket() { }
        
        public PongPacket(long originalTimestamp)
        {
            OriginalTimestamp = originalTimestamp;
            ServerTimestamp = DateTime.UtcNow.Ticks;
        }
    }
}
