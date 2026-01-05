using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Lobby
{
    /// <summary>
    /// 서버 인증 매니저 - 로그인/회원가입 처리
    /// </summary>
    public class AuthManager
    {
        private static AuthManager _instance;
        public static AuthManager Instance => _instance ??= new AuthManager();
        
        // 유저 정보 저장 (메모리 기반 - 실제로는 파일/DB 사용)
        private ConcurrentDictionary<string, UserCredentials> users = new();
        
        // 설정
        public int MaxPlayers { get; set; } = 100;
        public bool AllowRegistration { get; set; } = true;
        public bool UseWhitelist { get; set; } = false;
        public HashSet<string> Whitelist { get; set; } = new();
        public HashSet<string> Banlist { get; set; } = new();
        
        // 서버 정보
        public string ServerName { get; set; } = "Hybrid Multiplayer Server";
        public string ServerVersion { get; set; } = "0.2";
        
        // 이벤트
        public event Action<int, string> OnLoginSuccess;
        public event Action<int, string, LoginResult> OnLoginFailed;
        
        private AuthManager()
        {
            // 테스트용 기본 계정
            RegisterUser("test", HashPassword("1234"));
            RegisterUser("admin", HashPassword("admin"));
            HybridLogger.Log(LogCategory.Lobby, "AuthManager initialized");
        }
        
        /// <summary>
        /// 로그인 요청 처리
        /// </summary>
        public LoginResponsePacket HandleLogin(int clientId, LoginRequestPacket request, int currentOnlinePlayers)
        {
            var response = new LoginResponsePacket
            {
                ServerName = ServerName,
                ServerVersion = ServerVersion,
                OnlinePlayers = currentOnlinePlayers,
                MaxPlayers = MaxPlayers
            };
            
            // 유저명 검증
            if (string.IsNullOrEmpty(request.Username) || request.Username.Length < 2)
            {
                response.Result = LoginResult.InvalidCredentials;
                response.Message = "Invalid username";
                OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                return response;
            }
            
            // 서버 가득 참
            if (currentOnlinePlayers >= MaxPlayers)
            {
                response.Result = LoginResult.ServerFull;
                response.Message = "Server is full";
                OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                return response;
            }
            
            // 밴 체크
            if (Banlist.Contains(request.Username.ToLower()))
            {
                response.Result = LoginResult.Banned;
                response.Message = "You are banned from this server";
                OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                return response;
            }
            
            // 화이트리스트 체크
            if (UseWhitelist && !Whitelist.Contains(request.Username.ToLower()))
            {
                response.Result = LoginResult.NotWhitelisted;
                response.Message = "You are not whitelisted";
                OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                return response;
            }
            
            // 유저 존재 여부 확인
            if (users.TryGetValue(request.Username.ToLower(), out var credentials))
            {
                // 비밀번호 확인
                if (credentials.PasswordHash == request.PasswordHash)
                {
                    response.Result = LoginResult.Success;
                    response.SessionId = clientId;
                    response.Message = "Login successful";
                    
                    HybridLogger.Log(LogCategory.Lobby, 
                        $"Login successful: {request.Username} (ID: {clientId})");
                    OnLoginSuccess?.Invoke(clientId, request.Username);
                }
                else
                {
                    response.Result = LoginResult.InvalidCredentials;
                    response.Message = "Wrong password";
                    OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                }
            }
            else
            {
                // 새 유저 자동 등록
                if (AllowRegistration)
                {
                    if (RegisterUser(request.Username, request.PasswordHash))
                    {
                        response.Result = LoginResult.Registered;
                        response.SessionId = clientId;
                        response.Message = "Account created and logged in";
                        
                        HybridLogger.Log(LogCategory.Lobby, 
                            $"New user registered: {request.Username} (ID: {clientId})");
                        OnLoginSuccess?.Invoke(clientId, request.Username);
                    }
                    else
                    {
                        response.Result = LoginResult.InvalidCredentials;
                        response.Message = "Registration failed";
                        OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                    }
                }
                else
                {
                    response.Result = LoginResult.InvalidCredentials;
                    response.Message = "Account not found";
                    OnLoginFailed?.Invoke(clientId, request.Username, response.Result);
                }
            }
            
            return response;
        }
        
        /// <summary>
        /// 유저 등록
        /// </summary>
        public bool RegisterUser(string username, string passwordHash)
        {
            if (string.IsNullOrEmpty(username) || username.Length < 2)
                return false;
                
            var key = username.ToLower();
            if (users.ContainsKey(key))
                return false;
                
            return users.TryAdd(key, new UserCredentials
            {
                Username = username,
                PasswordHash = passwordHash,
                RegisteredAt = DateTime.UtcNow
            });
        }
        
        /// <summary>
        /// 비밀번호 해시
        /// </summary>
        public static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
        
        /// <summary>
        /// 밴 추가
        /// </summary>
        public void BanUser(string username)
        {
            Banlist.Add(username.ToLower());
            HybridLogger.Log(LogCategory.Lobby, $"User banned: {username}");
        }
        
        /// <summary>
        /// 밴 해제
        /// </summary>
        public void UnbanUser(string username)
        {
            Banlist.Remove(username.ToLower());
            HybridLogger.Log(LogCategory.Lobby, $"User unbanned: {username}");
        }
        
        /// <summary>
        /// 콘솔 상태 출력
        /// </summary>
        public void PrintStatus()
        {
            Console.WriteLine($"[AuthManager] Registered users: {users.Count}");
            Console.WriteLine($"[AuthManager] Banned users: {Banlist.Count}");
            Console.WriteLine($"[AuthManager] Max players: {MaxPlayers}");
            Console.WriteLine($"[AuthManager] Registration: {(AllowRegistration ? "enabled" : "disabled")}");
            Console.WriteLine($"[AuthManager] Whitelist: {(UseWhitelist ? "enabled" : "disabled")}");
        }
    }
    
    /// <summary>
    /// 유저 자격 증명
    /// </summary>
    public class UserCredentials
    {
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public DateTime RegisteredAt { get; set; }
        public DateTime LastLogin { get; set; }
        public bool IsAdmin { get; set; }
    }
}
