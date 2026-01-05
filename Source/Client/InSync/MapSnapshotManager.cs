using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Verse;
using RimWorld;
using HybridShared.Packets;

namespace HybridClient.InSync
{
    /// <summary>
    /// 맵 스냅샷 관리자
    /// MP SaveLoad.cs 참조 - 직접 XML 직렬화 사용
    /// </summary>
    public static class MapSnapshotManager
    {
        /// <summary>
        /// 맵 스냅샷 생성 (권위자가 호출)
        /// </summary>
        public static MapSnapshotPacket CreateSnapshot(Map map, int sessionId)
        {
            Log.Message($"[HybridMP][SNAPSHOT] Creating snapshot for map {map.uniqueID}");
            
            try
            {
                // 맵 직렬화 (직접 XML 생성)
                byte[] mapData = SerializeMap(map);
                
                // 압축
                byte[] compressed = CompressData(mapData);
                
                Log.Message($"[HybridMP][SNAPSHOT] Map serialized: {mapData.Length} bytes -> {compressed.Length} bytes compressed");
                
                var packet = new MapSnapshotPacket
                {
                    SessionId = sessionId,
                    MapId = map.uniqueID,
                    CurrentTick = Find.TickManager.TicksGame,
                    RandState = (ulong)Rand.Int
                };
                packet.SetCompressedMapData(compressed);
                
                return packet;
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][SNAPSHOT] Failed to create snapshot: {e}");
                return null;
            }
        }
        
        /// <summary>
        /// 맵 직렬화 - 권위자의 전체 게임 세이브 생성
        /// RimWorld의 세이브 시스템을 사용하여 전체 게임 상태를 직렬화
        /// </summary>
        private static byte[] SerializeMap(Map map)
        {
            try
            {
                // 임시 세이브 경로
                string tempSavePath = Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", "InSyncTemp.rws");
                
                // 게임 세이브 생성
                Log.Message($"[HybridMP][SNAPSHOT] Saving game to temp file for InSync...");
                
                // RimWorld 세이브 시스템 사용
                SafeSaver.Save(tempSavePath, "savegame", delegate
                {
                    ScribeMetaHeaderUtility.WriteMetaHeader();
                    Game game = Current.Game;
                    Scribe_Deep.Look(ref game, "game");
                });
                
                // 세이브 파일 읽기
                if (File.Exists(tempSavePath))
                {
                    byte[] saveData = File.ReadAllBytes(tempSavePath);
                    
                    // 임시 파일 삭제
                    try { File.Delete(tempSavePath); } catch { }
                    
                    Log.Message($"[HybridMP][SNAPSHOT] Game save serialized: {saveData.Length} bytes");
                    return saveData;
                }
                else
                {
                    Log.Error("[HybridMP][SNAPSHOT] Temp save file not created");
                    return null;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][SNAPSHOT] Failed to serialize game: {e}");
                return null;
            }
        }
        
        /// <summary>
        /// 맵의 폰 정보 기록
        /// </summary>
        private static void WriteMapPawns(Map map, XmlWriter writer)
        {
            writer.WriteStartElement("pawns");
            
            foreach (var pawn in map.mapPawns.AllPawns)
            {
                if (pawn?.Position == null)
                    continue;
                
                writer.WriteStartElement("pawn");
                writer.WriteAttributeString("id", pawn.thingIDNumber.ToString());
                writer.WriteAttributeString("def", pawn.def?.defName ?? "Unknown");
                writer.WriteAttributeString("x", pawn.Position.x.ToString());
                writer.WriteAttributeString("y", pawn.Position.y.ToString());
                writer.WriteAttributeString("z", pawn.Position.z.ToString());
                
                // 폰 상태
                if (pawn.Drafted)
                    writer.WriteAttributeString("drafted", "true");
                
                if (pawn.health?.summaryHealth != null)
                    writer.WriteAttributeString("health", pawn.health.summaryHealth.SummaryHealthPercent.ToString("F2"));
                
                // 세력 정보
                if (pawn.Faction != null)
                    writer.WriteAttributeString("faction", pawn.Faction.def.defName);
                
                // 폰 이름
                if (pawn.Name != null)
                    writer.WriteAttributeString("name", pawn.Name.ToStringShort);
                
                writer.WriteEndElement();
            }
            
            writer.WriteEndElement();
        }
        
        /// <summary>
        /// 맵 스냅샷 로드 (침입자가 호출)
        /// 권위자의 전체 게임 세이브를 로드
        /// </summary>
        public static bool LoadSnapshot(MapSnapshotPacket packet, out int mapId, out int startTick)
        {
            mapId = packet.MapId;
            startTick = packet.CurrentTick;
            
            Log.Message($"[HybridMP][SNAPSHOT] Loading snapshot - Session {packet.SessionId}, Map {packet.MapId}, Tick {packet.CurrentTick}");
            
            try
            {
                byte[] compressedData = packet.GetCompressedMapData();
                if (compressedData == null || compressedData.Length == 0)
                {
                    Log.Error("[HybridMP][SNAPSHOT] No map data in packet");
                    return false;
                }
                
                byte[] saveData = DecompressData(compressedData);
                Log.Message($"[HybridMP][SNAPSHOT] Decompressed: {compressedData.Length} -> {saveData.Length} bytes");
                
                // 데이터 유효성 검사
                if (saveData == null || saveData.Length < 100)
                {
                    Log.Error($"[HybridMP][SNAPSHOT] Invalid save data: {saveData?.Length ?? 0} bytes");
                    return false;
                }
                
                // 임시 세이브 파일로 저장
                string savesFolderPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves");
                if (!Directory.Exists(savesFolderPath))
                {
                    Directory.CreateDirectory(savesFolderPath);
                    Log.Message($"[HybridMP][SNAPSHOT] Created Saves folder: {savesFolderPath}");
                }
                
                string tempSavePath = Path.Combine(savesFolderPath, "InSyncReceived.rws");
                File.WriteAllBytes(tempSavePath, saveData);
                
                // 파일 저장 확인
                if (!File.Exists(tempSavePath))
                {
                    Log.Error($"[HybridMP][SNAPSHOT] Failed to write save file: {tempSavePath}");
                    return false;
                }
                
                var fileInfo = new FileInfo(tempSavePath);
                Log.Message($"[HybridMP][SNAPSHOT] Save file written: {tempSavePath} ({fileInfo.Length} bytes)");
                
                // GameDataSaveLoader.LoadGame은 내부적으로 LongEventHandler를 사용하므로
                // 여기서 직접 호출하고 반환 (파일 삭제는 하지 않음 - 게임 로드 중 필요)
                Log.Message("[HybridMP][SNAPSHOT] Starting game load...");
                GameDataSaveLoader.LoadGame("InSyncReceived");
                
                // NOTE: LoadGame은 비동기로 실행되므로 여기서 바로 반환
                // 파일 삭제는 나중에 수동으로 하거나, 다음 InSync 시 덮어쓰기됨
                // 난수 상태는 Lockstep 진입 시 동기화됨
                
                Log.Message($"[HybridMP][SNAPSHOT] Snapshot load initiated - MapId: {mapId}, StartTick: {startTick}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][SNAPSHOT] Failed to load snapshot: {e}");
                return false;
            }
        }
        
        /// <summary>
        /// 데이터 압축 (GZip)
        /// </summary>
        private static byte[] CompressData(byte[] data)
        {
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }
        
        /// <summary>
        /// 데이터 압축 해제
        /// </summary>
        private static byte[] DecompressData(byte[] compressedData)
        {
            using var inputStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();
            
            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }
    }
}
