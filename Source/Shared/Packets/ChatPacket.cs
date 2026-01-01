using System;
using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 채팅 메시지 패킷
    /// </summary>
    public class ChatPacket : PacketBase
    {
        public override PacketType Type => PacketType.Chat;
        
        /// <summary>발신자 이름 (서버에서 채움)</summary>
        public string SenderName { get; set; }
        
        /// <summary>메시지 내용</summary>
        public string Message { get; set; }
        
        /// <summary>메시지 타입</summary>
        public ChatMessageType MessageType { get; set; } = ChatMessageType.Normal;
        
        /// <summary>전송 시간</summary>
        public DateTime Timestamp { get; set; }
        
        public ChatPacket() 
        {
            Timestamp = DateTime.UtcNow;
        }
        
        public ChatPacket(string message, ChatMessageType type = ChatMessageType.Normal)
        {
            Message = message;
            MessageType = type;
            Timestamp = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 채팅 메시지 타입
    /// </summary>
    public enum ChatMessageType : byte
    {
        /// <summary>일반 채팅</summary>
        Normal = 0,
        /// <summary>시스템 메시지</summary>
        System = 1,
        /// <summary>귓속말</summary>
        Whisper = 2,
        /// <summary>서버 공지</summary>
        Broadcast = 3
    }
    
    /// <summary>
    /// 접속 플레이어 목록
    /// </summary>
    public class PlayerListPacket : PacketBase
    {
        public override PacketType Type => PacketType.PlayerList;
        
        /// <summary>접속 중인 플레이어 목록</summary>
        public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();
    }
    
    /// <summary>
    /// 플레이어 정보
    /// </summary>
    public class PlayerInfo
    {
        public int SessionId { get; set; }
        public string Username { get; set; }
        public int Ping { get; set; }
        public bool InBattle { get; set; }
    }
}
