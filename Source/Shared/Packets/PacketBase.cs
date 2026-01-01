using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace HybridShared.Packets
{
    /// <summary>
    /// 모든 패킷의 베이스 클래스
    /// </summary>
    public abstract class PacketBase
    {
        /// <summary>패킷 타입</summary>
        public abstract PacketType Type { get; }
        
        /// <summary>
        /// 패킷을 byte[]로 직렬화
        /// </summary>
        public byte[] Serialize()
        {
            return PacketSerializer.Serialize(this);
        }
    }
    
    /// <summary>
    /// 패킷 직렬화/역직렬화 유틸리티
    /// </summary>
    public static class PacketSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore
        };
        
        /// <summary>
        /// 패킷을 byte[]로 직렬화
        /// 포맷: [1byte 타입][N bytes JSON 데이터]
        /// </summary>
        public static byte[] Serialize(PacketBase packet)
        {
            string json = JsonConvert.SerializeObject(packet, Settings);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            
            // 첫 바이트: 패킷 타입
            byte[] result = new byte[1 + jsonBytes.Length];
            result[0] = (byte)packet.Type;
            Buffer.BlockCopy(jsonBytes, 0, result, 1, jsonBytes.Length);
            
            return result;
        }
        
        /// <summary>
        /// byte[]를 패킷으로 역직렬화
        /// </summary>
        public static PacketBase Deserialize(byte[] data)
        {
            if (data == null || data.Length < 2)
                return null;
                
            PacketType type = (PacketType)data[0];
            string json = Encoding.UTF8.GetString(data, 1, data.Length - 1);
            
            return type switch
            {
                PacketType.Handshake => JsonConvert.DeserializeObject<HandshakePacket>(json, Settings),
                PacketType.HandshakeResponse => JsonConvert.DeserializeObject<HandshakeResponsePacket>(json, Settings),
                PacketType.Ping => JsonConvert.DeserializeObject<PingPacket>(json, Settings),
                PacketType.Pong => JsonConvert.DeserializeObject<PongPacket>(json, Settings),
                PacketType.Chat => JsonConvert.DeserializeObject<ChatPacket>(json, Settings),
                PacketType.PlayerList => JsonConvert.DeserializeObject<PlayerListPacket>(json, Settings),
                PacketType.WorldState => JsonConvert.DeserializeObject<WorldStatePacket>(json, Settings),
                PacketType.MapState => JsonConvert.DeserializeObject<MapStatePacket>(json, Settings),
                PacketType.PawnState => JsonConvert.DeserializeObject<PawnStatePacket>(json, Settings),
                PacketType.SyncField => JsonConvert.DeserializeObject<ThingDeltaPacket>(json, Settings),
                PacketType.SyncAction => JsonConvert.DeserializeObject<DeltaBatchPacket>(json, Settings),
                PacketType.SyncCommand => JsonConvert.DeserializeObject<ClientStatePacket>(json, Settings),
                PacketType.FastResync => JsonConvert.DeserializeObject<AuthoritativeStatePacket>(json, Settings),
                _ => null
            };
        }
        
        /// <summary>
        /// 특정 타입으로 역직렬화
        /// </summary>
        public static T Deserialize<T>(byte[] data) where T : PacketBase
        {
            return Deserialize(data) as T;
        }
        
        /// <summary>
        /// 대용량 데이터 압축
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            {
                gzip.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }
        
        /// <summary>
        /// 압축 해제
        /// </summary>
        public static byte[] Decompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }
}
