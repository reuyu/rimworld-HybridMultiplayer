using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Verse;

namespace HybridClient
{
    /// <summary>
    /// 맵 직렬화/역직렬화 유틸리티
    /// Multiplayer 모드의 SaveLoad.cs를 참조하여 구현
    /// </summary>
    public static class MapSerializer
    {
        /// <summary>
        /// 현재 맵을 byte[]로 직렬화
        /// </summary>
        public static byte[] SerializeMap(Map map)
        {
            if (map == null)
            {
                Log.Error("[HybridMP] Cannot serialize null map");
                return null;
            }
            
            try
            {
                // Scribe를 사용하여 XML로 직렬화
                var doc = SaveMapToXml(map);
                
                if (doc == null)
                    return null;
                
                // XML을 byte[]로 변환
                byte[] xmlBytes = XmlToByteArray(doc);
                
                // GZip 압축
                byte[] compressed = Compress(xmlBytes);
                
                Log.Message($"[HybridMP] Map serialized: {xmlBytes.Length} bytes -> {compressed.Length} bytes (compressed)");
                
                return compressed;
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] Failed to serialize map: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// byte[]를 맵으로 역직렬화 (로드는 별도 처리 필요)
        /// </summary>
        public static byte[] DeserializeMapData(byte[] compressedData)
        {
            try
            {
                return Decompress(compressedData);
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] Failed to decompress map data: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 맵을 XML 문서로 저장
        /// </summary>
        private static XmlDocument SaveMapToXml(Map map)
        {
            try
            {
                // MemoryStream에 저장
                using var stream = new MemoryStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                
                // XML 생성
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = Encoding.UTF8
                };
                
                using var xmlWriter = XmlWriter.Create(writer, settings);
                
                xmlWriter.WriteStartDocument();
                xmlWriter.WriteStartElement("MapSnapshot");
                
                // 맵 기본 정보
                xmlWriter.WriteElementString("uniqueID", map.uniqueID.ToString());
                xmlWriter.WriteElementString("tileID", map.Tile.ToString());
                xmlWriter.WriteElementString("mapSize", $"{map.Size.x},{map.Size.y},{map.Size.z}");
                
                // 폰 목록 (기본 정보만)
                xmlWriter.WriteStartElement("pawns");
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    xmlWriter.WriteStartElement("pawn");
                    xmlWriter.WriteElementString("thingID", pawn.thingIDNumber.ToString());
                    xmlWriter.WriteElementString("defName", pawn.def.defName);
                    xmlWriter.WriteElementString("position", $"{pawn.Position.x},{pawn.Position.y},{pawn.Position.z}");
                    xmlWriter.WriteElementString("faction", pawn.Faction?.Name ?? "None");
                    xmlWriter.WriteEndElement();
                }
                xmlWriter.WriteEndElement();
                
                xmlWriter.WriteEndElement();
                xmlWriter.WriteEndDocument();
                xmlWriter.Flush();
                
                // MemoryStream에서 XML 문서 생성
                stream.Position = 0;
                var doc = new XmlDocument();
                doc.Load(stream);
                
                return doc;
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] SaveMapToXml failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// XML 문서를 byte[]로 변환
        /// </summary>
        public static byte[] XmlToByteArray(XmlDocument doc)
        {
            using var stream = new MemoryStream();
            doc.Save(stream);
            return stream.ToArray();
        }
        
        /// <summary>
        /// byte[]를 XML 문서로 변환
        /// </summary>
        public static XmlDocument ByteArrayToXml(byte[] data)
        {
            var doc = new XmlDocument();
            using var stream = new MemoryStream(data);
            doc.Load(stream);
            return doc;
        }
        
        /// <summary>
        /// GZip 압축
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
        /// GZip 압축 해제
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
