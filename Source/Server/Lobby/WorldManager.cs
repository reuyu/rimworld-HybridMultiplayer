using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Lobby
{
    /// <summary>
    /// 서버 월드 매니저 - 월드 데이터 및 정착지 관리
    /// RT WorldManager 기반 - HybridMP 적응
    /// </summary>
    public class WorldManager
    {
        private static WorldManager _instance;
        public static WorldManager Instance => _instance ??= new WorldManager();
        
        // 월드 설정 파일 경로
        private readonly string worldConfigPath;
        
        // 현재 월드 데이터
        public PlanetConfig CurrentWorld { get; private set; }
        
        // 월드 존재 여부
        public bool HasWorld => CurrentWorld != null;
        
        // 이벤트
        public event Action<PlanetConfig> OnWorldCreated;
        public event Action<PlayerSettlementInfo> OnSettlementCreated;
        public event Action<int> OnSettlementRemoved; // TileId
        
        private WorldManager()
        {
            worldConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "world.json");
            LoadWorld();
            HybridLogger.Log(LogCategory.Lobby, "WorldManager initialized");
        }
        
        /// <summary>
        /// 월드 파일에서 로드
        /// </summary>
        private void LoadWorld()
        {
            try
            {
                if (File.Exists(worldConfigPath))
                {
                    var json = File.ReadAllText(worldConfigPath);
                    CurrentWorld = JsonSerializer.Deserialize<PlanetConfig>(json);
                    HybridLogger.Log(LogCategory.Lobby, 
                        $"World loaded: {CurrentWorld?.SeedString ?? "unknown"}, " +
                        $"{CurrentWorld?.PlayerSettlements?.Count ?? 0} player settlements");
                }
                else
                {
                    HybridLogger.Log(LogCategory.Lobby, "No world file found - waiting for first player to create");
                }
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Lobby, $"Failed to load world: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 월드 파일로 저장
        /// </summary>
        public void SaveWorld()
        {
            if (CurrentWorld == null) return;
            
            try
            {
                var dir = Path.GetDirectoryName(worldConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(CurrentWorld, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(worldConfigPath, json);
                HybridLogger.Log(LogCategory.Lobby, "World saved to file");
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Lobby, $"Failed to save world: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 클라이언트에게 월드 요청 (첫 접속자가 생성)
        /// </summary>
        public WorldPacket CreateWorldRequestPacket()
        {
            return new WorldPacket
            {
                StepMode = WorldStepMode.RequestCreate
            };
        }
        
        /// <summary>
        /// 클라이언트에게 기존 월드 전송
        /// </summary>
        public WorldPacket CreateWorldSendPacket()
        {
            if (CurrentWorld == null)
            {
                HybridLogger.Warn(LogCategory.Lobby, "Attempted to send world but none exists");
                return null;
            }
            
            var json = JsonSerializer.SerializeToUtf8Bytes(CurrentWorld);
            return new WorldPacket
            {
                StepMode = WorldStepMode.SendToClient,
                WorldData = json
            };
        }
        
        /// <summary>
        /// 클라이언트로부터 월드 수신 (첫 생성)
        /// </summary>
        public void ReceiveWorldFromClient(byte[] worldData, string creatorUsername)
        {
            try
            {
                CurrentWorld = JsonSerializer.Deserialize<PlanetConfig>(worldData);
                CurrentWorld.PlayerSettlements ??= new List<PlayerSettlementInfo>();
                
                SaveWorld();
                
                HybridLogger.Log(LogCategory.Lobby, 
                    $"World created by {creatorUsername}: Seed={CurrentWorld.SeedString}");
                
                OnWorldCreated?.Invoke(CurrentWorld);
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Lobby, $"Failed to receive world: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 정착지 생성 요청 처리
        /// </summary>
        public SettlementCreateResponsePacket HandleSettlementCreate(int clientId, string username, SettlementCreatePacket request)
        {
            var response = new SettlementCreateResponsePacket();
            
            // 월드 확인
            if (CurrentWorld == null)
            {
                response.Success = false;
                response.Message = "No world exists";
                return response;
            }
            
            // 타일 점유 확인
            if (IsTileOccupied(request.TileId))
            {
                response.Success = false;
                response.Message = "Tile is already occupied";
                return response;
            }
            
            // 정착지 생성
            var settlement = new PlayerSettlementInfo
            {
                TileId = request.TileId,
                SettlementName = request.SettlementName ?? $"{username}'s Colony",
                OwnerUsername = username,
                OwnerId = clientId,
                CreatedAt = DateTime.UtcNow
            };
            
            CurrentWorld.PlayerSettlements.Add(settlement);
            SaveWorld();
            
            HybridLogger.Log(LogCategory.Lobby, 
                $"Settlement created: {settlement.SettlementName} at tile {settlement.TileId} by {username}");
            
            response.Success = true;
            response.Message = "Settlement created";
            response.Settlement = settlement;
            
            OnSettlementCreated?.Invoke(settlement);
            
            return response;
        }
        
        /// <summary>
        /// 타일 점유 확인
        /// </summary>
        public bool IsTileOccupied(int tileId)
        {
            if (CurrentWorld == null) return false;
            
            // 플레이어 정착지 확인
            foreach (var s in CurrentWorld.PlayerSettlements)
            {
                if (s.TileId == tileId) return true;
            }
            
            // NPC 정착지 확인
            foreach (var s in CurrentWorld.NPCSettlements)
            {
                if (s.TileId == tileId) return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 정착지 제거
        /// </summary>
        public bool RemoveSettlement(int tileId)
        {
            if (CurrentWorld == null) return false;
            
            var removed = CurrentWorld.PlayerSettlements.RemoveAll(s => s.TileId == tileId);
            if (removed > 0)
            {
                SaveWorld();
                OnSettlementRemoved?.Invoke(tileId);
                HybridLogger.Log(LogCategory.Lobby, $"Settlement removed at tile {tileId}");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// 정착지 제거 (소유자 확인)
        /// </summary>
        public bool RemoveSettlement(string username, int tileId)
        {
            if (CurrentWorld == null) return false;
            
            var removed = CurrentWorld.PlayerSettlements.RemoveAll(s => 
                s.TileId == tileId && s.OwnerUsername == username);
            if (removed > 0)
            {
                SaveWorld();
                OnSettlementRemoved?.Invoke(tileId);
                HybridLogger.Log(LogCategory.Lobby, $"Settlement removed at tile {tileId} by {username}");
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// 정착지 목록 패킷 생성
        /// </summary>
        public SettlementListPacket CreateSettlementListPacket()
        {
            return new SettlementListPacket
            {
                Settlements = CurrentWorld?.PlayerSettlements ?? new List<PlayerSettlementInfo>()
            };
        }
        
        /// <summary>
        /// 콘솔 상태 출력
        /// </summary>
        public void PrintStatus()
        {
            Console.WriteLine($"[WorldManager] Has world: {HasWorld}");
            if (HasWorld)
            {
                Console.WriteLine($"  Seed: {CurrentWorld.SeedString}");
                Console.WriteLine($"  Player settlements: {CurrentWorld.PlayerSettlements?.Count ?? 0}");
                Console.WriteLine($"  NPC settlements: {CurrentWorld.NPCSettlements?.Count ?? 0}");
                Console.WriteLine($"  NPC factions: {CurrentWorld.NPCFactions?.Count ?? 0}");
                
                foreach (var s in CurrentWorld.PlayerSettlements ?? new List<PlayerSettlementInfo>())
                {
                    Console.WriteLine($"    - {s.SettlementName} (Tile: {s.TileId}, Owner: {s.OwnerUsername})");
                }
            }
        }
    }
}
