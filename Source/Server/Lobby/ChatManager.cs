using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Lobby
{
    /// <summary>
    /// 서버 채팅 매니저 - 글로벌/귓속말/로컬 채팅 관리
    /// </summary>
    public class ChatManager
    {
        private static ChatManager _instance;
        public static ChatManager Instance => _instance ??= new ChatManager();
        
        // 채팅 기록 (최근 100개)
        private Queue<ChatMessage> chatHistory = new();
        private const int MaxHistorySize = 100;
        
        // 채팅 쿨다운 (스팸 방지)
        private ConcurrentDictionary<int, DateTime> lastChatTime = new();
        private TimeSpan chatCooldown = TimeSpan.FromMilliseconds(500);
        
        // 채팅 브로드캐스트 이벤트
        public event Action<ChatPacket, int[]> OnBroadcast; // (packet, targetClientIds)
        
        private ChatManager()
        {
            HybridLogger.Log(LogCategory.Chat, "ChatManager initialized");
        }
        
        /// <summary>
        /// 글로벌 채팅 메시지 처리
        /// </summary>
        public void HandleGlobalChat(int senderId, string senderName, string message)
        {
            // 쿨다운 체크
            if (!CheckCooldown(senderId))
            {
                return;
            }
            
            // 메시지 검증
            if (string.IsNullOrEmpty(message) || message.Length > 500)
            {
                return;
            }
            
            var chatMessage = new ChatMessage
            {
                SenderId = senderId,
                SenderName = senderName,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Type = ChatType.Global
            };
            
            AddToHistory(chatMessage);
            
            HybridLogger.Log(LogCategory.Chat, 
                $"[GLOBAL] {senderName}: {message}");
            
            // 모든 클라이언트에게 브로드캐스트
            var packet = new ChatPacket
            {
                SenderName = senderName,
                Message = message,
                Timestamp = chatMessage.Timestamp
            };
            
            OnBroadcast?.Invoke(packet, null); // null = 모든 클라이언트
        }
        
        /// <summary>
        /// 귓속말 처리
        /// </summary>
        public void HandleWhisper(int senderId, string senderName, int targetId, string message)
        {
            if (!CheckCooldown(senderId)) return;
            if (string.IsNullOrEmpty(message) || message.Length > 500) return;
            
            var chatMessage = new ChatMessage
            {
                SenderId = senderId,
                SenderName = senderName,
                TargetId = targetId,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Type = ChatType.Whisper
            };
            
            HybridLogger.Log(LogCategory.Chat, 
                $"[WHISPER] {senderName} -> {targetId}: {message}");
            
            var packet = new ChatPacket
            {
                SenderName = senderName,
                Message = $"[귓속말] {message}",
                Timestamp = chatMessage.Timestamp
            };
            
            // 발신자와 수신자에게만 전송
            OnBroadcast?.Invoke(packet, new[] { senderId, targetId });
        }
        
        /// <summary>
        /// 로컬 채팅 (같은 맵/타일)
        /// </summary>
        public void HandleLocalChat(int senderId, string senderName, string message, int[] nearbyPlayerIds)
        {
            if (!CheckCooldown(senderId)) return;
            if (string.IsNullOrEmpty(message) || message.Length > 500) return;
            
            var chatMessage = new ChatMessage
            {
                SenderId = senderId,
                SenderName = senderName,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Type = ChatType.Local
            };
            
            HybridLogger.Log(LogCategory.Chat, 
                $"[LOCAL] {senderName}: {message} (to {nearbyPlayerIds?.Length ?? 0} players)");
            
            var packet = new ChatPacket
            {
                SenderName = senderName,
                Message = $"[로컬] {message}",
                Timestamp = chatMessage.Timestamp
            };
            
            OnBroadcast?.Invoke(packet, nearbyPlayerIds);
        }
        
        /// <summary>
        /// 시스템 메시지
        /// </summary>
        public void SendSystemMessage(string message, int[] targetIds = null)
        {
            var packet = new ChatPacket
            {
                SenderName = "[시스템]",
                Message = message,
                Timestamp = DateTime.UtcNow
            };
            
            HybridLogger.Log(LogCategory.Chat, $"[SYSTEM] {message}");
            OnBroadcast?.Invoke(packet, targetIds);
        }
        
        /// <summary>
        /// 쿨다운 체크
        /// </summary>
        private bool CheckCooldown(int clientId)
        {
            var now = DateTime.UtcNow;
            
            if (lastChatTime.TryGetValue(clientId, out var lastTime))
            {
                if (now - lastTime < chatCooldown)
                {
                    return false; // 쿨다운 중
                }
            }
            
            lastChatTime[clientId] = now;
            return true;
        }
        
        /// <summary>
        /// 기록에 추가
        /// </summary>
        private void AddToHistory(ChatMessage message)
        {
            chatHistory.Enqueue(message);
            while (chatHistory.Count > MaxHistorySize)
            {
                chatHistory.Dequeue();
            }
        }
        
        /// <summary>
        /// 최근 채팅 기록 가져오기
        /// </summary>
        public List<ChatMessage> GetRecentHistory(int count = 20)
        {
            return chatHistory.TakeLast(count).ToList();
        }
    }
    
    /// <summary>
    /// 채팅 메시지 기록
    /// </summary>
    public class ChatMessage
    {
        public int SenderId { get; set; }
        public string SenderName { get; set; }
        public int? TargetId { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public ChatType Type { get; set; }
    }
    
    /// <summary>
    /// 채팅 타입
    /// </summary>
    public enum ChatType
    {
        Global,
        Whisper,
        Local,
        System
    }
}
