using System;
using System.Collections.Generic;
using HybridShared;
using HybridShared.Packets;

namespace HybridClient.Handlers
{
    /// <summary>
    /// 클라이언트 패킷 라우터
    /// </summary>
    public class ClientPacketRouter
    {
        private readonly Dictionary<PacketType, Action<PacketBase>> handlers = new();
        
        /// <summary>
        /// 패킷 핸들러 등록
        /// </summary>
        public void Register<T>(Action<T> handler) where T : PacketBase
        {
            var temp = Activator.CreateInstance<T>();
            handlers[temp.Type] = packet => handler((T)packet);
        }
        
        /// <summary>
        /// 수신된 데이터 처리
        /// </summary>
        public void HandlePacket(byte[] data)
        {
            var packet = PacketSerializer.Deserialize(data);
            
            if (packet == null)
            {
                // 디버그: 어떤 패킷이 실패했는지 확인
                int packetType = data.Length > 0 ? data[0] : -1;
                Log($"[!] Failed to deserialize packet (Type byte: {packetType}, Length: {data.Length})");
                return;
            }
            
            if (handlers.TryGetValue(packet.Type, out var handler))
            {
                try
                {
                    handler(packet);
                }
                catch (Exception ex)
                {
                    Log($"[!] Error handling {packet.Type}: {ex.Message}");
                }
            }
            else
            {
                Log($"[?] No handler for packet type: {packet.Type}");
            }
        }
        
        private void Log(string message)
        {
            // RimWorld에서는 Verse.Log 사용
            #if CLIENT
            Verse.Log.Message($"[HybridMP] {message}");
            #else
            Console.WriteLine(message);
            #endif
        }
    }
}
