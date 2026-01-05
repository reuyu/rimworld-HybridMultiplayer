using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Lobby
{
    /// <summary>
    /// 플레이어 매니저 - 접속 플레이어 상태 및 정보 관리
    /// </summary>
    public class PlayerManager
    {
        private static PlayerManager _instance;
        public static PlayerManager Instance => _instance ??= new PlayerManager();
        
        // 플레이어 정보 (ClientId -> PlayerInfo)
        private ConcurrentDictionary<int, PlayerInfo> players = new();
        
        // 이벤트
        public event Action<int, PlayerInfo> OnPlayerJoined;
        public event Action<int, PlayerInfo> OnPlayerLeft;
        public event Action<int, PlayerState> OnPlayerStateChanged;
        
        public int OnlineCount => players.Count;
        public int InGameCount => players.Values.Count(p => p.State == PlayerState.InGame);
        public int InSyncCount => players.Values.Count(p => p.State == PlayerState.InSync);
        
        private PlayerManager()
        {
            HybridLogger.Log(LogCategory.Player, "PlayerManager initialized");
        }
        
        /// <summary>
        /// 플레이어 등록
        /// </summary>
        public PlayerInfo RegisterPlayer(int clientId, string username)
        {
            var info = new PlayerInfo
            {
                ClientId = clientId,
                Username = username,
                State = PlayerState.Online,
                JoinedAt = DateTime.UtcNow
            };
            
            players[clientId] = info;
            
            HybridLogger.Log(LogCategory.Player, 
                $"Player registered: {username} (ID: {clientId})");
            
            OnPlayerJoined?.Invoke(clientId, info);
            
            return info;
        }
        
        /// <summary>
        /// 플레이어 해제
        /// </summary>
        public void UnregisterPlayer(int clientId)
        {
            if (players.TryRemove(clientId, out var info))
            {
                HybridLogger.Log(LogCategory.Player, 
                    $"Player unregistered: {info.Username} (ID: {clientId})");
                
                OnPlayerLeft?.Invoke(clientId, info);
            }
        }
        
        /// <summary>
        /// 플레이어 정보 가져오기
        /// </summary>
        public PlayerInfo Get(int clientId)
        {
            return players.GetValueOrDefault(clientId);
        }
        
        /// <summary>
        /// 이름으로 플레이어 찾기
        /// </summary>
        public PlayerInfo GetByUsername(string username)
        {
            return players.Values.FirstOrDefault(p => 
                p.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// 상태 변경
        /// </summary>
        public void SetState(int clientId, PlayerState newState)
        {
            if (players.TryGetValue(clientId, out var info))
            {
                var oldState = info.State;
                info.State = newState;
                
                HybridLogger.Log(LogCategory.Player, 
                    $"Player state changed: {info.Username}: {oldState} -> {newState}");
                
                OnPlayerStateChanged?.Invoke(clientId, newState);
            }
        }
        
        /// <summary>
        /// 정착지 정보 업데이트
        /// </summary>
        public void UpdateSettlement(int clientId, string settlementName, int worldTileId)
        {
            if (players.TryGetValue(clientId, out var info))
            {
                info.SettlementName = settlementName;
                info.WorldTileId = worldTileId;
            }
        }
        
        /// <summary>
        /// InSync 세션 설정
        /// </summary>
        public void SetInSyncSession(int clientId, string sessionId)
        {
            if (players.TryGetValue(clientId, out var info))
            {
                info.InSyncSessionId = sessionId;
                if (!string.IsNullOrEmpty(sessionId))
                {
                    info.State = PlayerState.InSync;
                }
                else
                {
                    info.State = PlayerState.InGame;
                }
            }
        }
        
        /// <summary>
        /// 같은 타일에 있는 플레이어들 찾기
        /// </summary>
        public List<PlayerInfo> GetPlayersOnTile(int tileId, int? excludeClientId = null)
        {
            return players.Values
                .Where(p => p.WorldTileId == tileId && p.ClientId != excludeClientId)
                .ToList();
        }
        
        /// <summary>
        /// InSync 세션에 있는 플레이어들 찾기
        /// </summary>
        public List<PlayerInfo> GetPlayersInSession(string sessionId)
        {
            return players.Values
                .Where(p => p.InSyncSessionId == sessionId)
                .ToList();
        }
        
        /// <summary>
        /// 모든 플레이어 목록 (패킷용)
        /// </summary>
        public List<HybridShared.Packets.PlayerInfo> GetPlayerListEntries()
        {
            return players.Values.Select(p => new HybridShared.Packets.PlayerInfo
            {
                SessionId = p.ClientId,
                Username = p.Username,
                Ping = p.Ping
            }).ToList();
        }
        
        /// <summary>
        /// Ping 업데이트
        /// </summary>
        public void UpdatePing(int clientId, int ping)
        {
            if (players.TryGetValue(clientId, out var info))
            {
                info.Ping = ping;
            }
        }
        
        /// <summary>
        /// 콘솔 상태 출력
        /// </summary>
        public void PrintStatus()
        {
            Console.WriteLine($"[PlayerManager] Total players: {OnlineCount}");
            Console.WriteLine($"  - InGame: {InGameCount}");
            Console.WriteLine($"  - InSync: {InSyncCount}");
            foreach (var p in players.Values)
            {
                Console.WriteLine($"  - {p.Username} (ID: {p.ClientId}): {p.State}, Tile: {p.WorldTileId}");
            }
        }
    }
    
    /// <summary>
    /// 플레이어 정보
    /// </summary>
    public class PlayerInfo
    {
        public int ClientId { get; set; }
        public string Username { get; set; }
        public PlayerState State { get; set; }
        public DateTime JoinedAt { get; set; }
        public int Ping { get; set; }
        
        // 게임 내 정보
        public string SettlementName { get; set; }
        public int WorldTileId { get; set; }
        public string InSyncSessionId { get; set; }
        
        public TimeSpan PlayTime => DateTime.UtcNow - JoinedAt;
    }
    
    /// <summary>
    /// 플레이어 상태
    /// </summary>
    public enum PlayerState
    {
        /// <summary>접속만 됨</summary>
        Online,
        /// <summary>게임 중 (비동기)</summary>
        InGame,
        /// <summary>InSync 모드 (실시간 동기화)</summary>
        InSync,
        /// <summary>AFK (자리비움)</summary>
        Away
    }
}
