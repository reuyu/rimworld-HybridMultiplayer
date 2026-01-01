using System;
using System.Collections.Generic;
using LiteNetLib;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Handlers
{
    /// <summary>
    /// 서버 패킷 라우터 - 패킷 타입에 따라 핸들러 호출
    /// </summary>
    public class ServerPacketRouter
    {
        private readonly Dictionary<PacketType, Action<NetPeer, PacketBase>> handlers = new();
        
        /// <summary>
        /// 패킷 핸들러 등록
        /// </summary>
        public void Register<T>(Action<NetPeer, T> handler) where T : PacketBase
        {
            // 임시 인스턴스로 타입 확인
            var temp = Activator.CreateInstance<T>();
            handlers[temp.Type] = (peer, packet) => handler(peer, (T)packet);
        }
        
        /// <summary>
        /// 수신된 데이터 처리
        /// </summary>
        public void HandlePacket(NetPeer peer, byte[] data)
        {
            var packet = PacketSerializer.Deserialize(data);
            
            if (packet == null)
            {
                Console.WriteLine($"[!] Failed to deserialize packet from {peer.EndPoint}");
                return;
            }
            
            if (handlers.TryGetValue(packet.Type, out var handler))
            {
                try
                {
                    handler(peer, packet);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Error handling {packet.Type}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[?] No handler for packet type: {packet.Type}");
            }
        }
    }
}
