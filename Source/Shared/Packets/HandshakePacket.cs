using System;

namespace HybridShared.Packets
{
    /// <summary>
    /// 클라이언트 → 서버: 인증 요청
    /// </summary>
    public class HandshakePacket : PacketBase
    {
        public override PacketType Type => PacketType.Handshake;
        
        /// <summary>유저명</summary>
        public string Username { get; set; }
        
        /// <summary>클라이언트 프로토콜 버전</summary>
        public int ProtocolVersion { get; set; } = PacketConst.ProtocolVersion;
        
        /// <summary>모드 리스트 해시 (호환성 체크용)</summary>
        public string ModListHash { get; set; }
        
        public HandshakePacket() { }
        
        public HandshakePacket(string username, string modListHash = null)
        {
            Username = username;
            ModListHash = modListHash;
        }
    }
    
    /// <summary>
    /// 서버 → 클라이언트: 인증 응답
    /// </summary>
    public class HandshakeResponsePacket : PacketBase
    {
        public override PacketType Type => PacketType.HandshakeResponse;
        
        /// <summary>인증 성공 여부</summary>
        public bool Success { get; set; }
        
        /// <summary>세션 ID (접속 후 식별용)</summary>
        public int SessionId { get; set; }
        
        /// <summary>실패 사유</summary>
        public string Message { get; set; }
        
        /// <summary>서버 이름</summary>
        public string ServerName { get; set; }
        
        /// <summary>현재 접속자 수</summary>
        public int PlayerCount { get; set; }
        
        public HandshakeResponsePacket() { }
        
        public static HandshakeResponsePacket CreateSuccess(int sessionId, string serverName, int playerCount)
        {
            return new HandshakeResponsePacket
            {
                Success = true,
                SessionId = sessionId,
                ServerName = serverName,
                PlayerCount = playerCount
            };
        }
        
        public static HandshakeResponsePacket CreateFailed(string message)
        {
            return new HandshakeResponsePacket
            {
                Success = false,
                Message = message
            };
        }
    }
}
