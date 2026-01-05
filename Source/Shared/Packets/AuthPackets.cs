using System;
using System.Collections.Generic;

namespace HybridShared.Packets
{
    /// <summary>
    /// 로그인 요청 패킷.
    /// 클라이언트 → 서버: 유저명/비밀번호 인증 요청.
    /// </summary>
    public class LoginRequestPacket : PacketBase
    {
        public override PacketType Type => PacketType.LoginRequest;
        
        /// <summary>유저명</summary>
        public string Username { get; set; }
        
        /// <summary>비밀번호 해시</summary>
        public string PasswordHash { get; set; }
        
        /// <summary>클라이언트 버전</summary>
        public string ClientVersion { get; set; }
        
        /// <summary>모드 리스트 (모드 호환성 체크용)</summary>
        public List<string> ModList { get; set; } = new();
    }
    
    /// <summary>
    /// 로그인 응답 패킷.
    /// 서버 → 클라이언트: 인증 결과.
    /// </summary>
    public class LoginResponsePacket : PacketBase
    {
        public override PacketType Type => PacketType.LoginResponse;
        
        /// <summary>로그인 결과</summary>
        public LoginResult Result { get; set; }
        
        /// <summary>성공 시 세션 ID</summary>
        public int SessionId { get; set; }
        
        /// <summary>서버 이름</summary>
        public string ServerName { get; set; }
        
        /// <summary>추가 메시지 (오류 설명 등)</summary>
        public string Message { get; set; }
        
        /// <summary>서버 버전</summary>
        public string ServerVersion { get; set; }
        
        /// <summary>접속 중인 플레이어 수</summary>
        public int OnlinePlayers { get; set; }
        
        /// <summary>최대 플레이어 수</summary>
        public int MaxPlayers { get; set; }
    }
    
    /// <summary>
    /// 로그인 결과
    /// </summary>
    public enum LoginResult : byte
    {
        /// <summary>성공</summary>
        Success = 0,
        
        /// <summary>유저명/비밀번호 불일치</summary>
        InvalidCredentials = 1,
        
        /// <summary>밴됨</summary>
        Banned = 2,
        
        /// <summary>중복 접속 (다른 곳에서 접속 중)</summary>
        AlreadyConnected = 3,
        
        /// <summary>서버 가득 참</summary>
        ServerFull = 4,
        
        /// <summary>화이트리스트 필요</summary>
        NotWhitelisted = 5,
        
        /// <summary>버전 불일치</summary>
        VersionMismatch = 6,
        
        /// <summary>모드 불일치</summary>
        ModMismatch = 7,
        
        /// <summary>서버 설정 중</summary>
        ServerNotReady = 8,
        
        /// <summary>신규 등록 완료</summary>
        Registered = 9
    }
    
    /// <summary>
    /// 회원가입 요청 패킷.
    /// 클라이언트 → 서버: 새 계정 생성.
    /// </summary>
    public class RegisterRequestPacket : PacketBase
    {
        public override PacketType Type => PacketType.RegisterRequest;
        
        /// <summary>유저명</summary>
        public string Username { get; set; }
        
        /// <summary>비밀번호 해시</summary>
        public string PasswordHash { get; set; }
    }
    
    /// <summary>
    /// 회원가입 응답 패킷.
    /// 서버 → 클라이언트: 등록 결과.
    /// </summary>
    public class RegisterResponsePacket : PacketBase
    {
        public override PacketType Type => PacketType.RegisterResponse;
        
        /// <summary>등록 결과</summary>
        public RegisterResult Result { get; set; }
        
        /// <summary>메시지</summary>
        public string Message { get; set; }
    }
    
    /// <summary>
    /// 회원가입 결과
    /// </summary>
    public enum RegisterResult : byte
    {
        /// <summary>성공</summary>
        Success = 0,
        
        /// <summary>이미 존재하는 유저명</summary>
        UsernameExists = 1,
        
        /// <summary>잘못된 유저명 형식</summary>
        InvalidUsername = 2,
        
        /// <summary>잘못된 비밀번호 형식</summary>
        InvalidPassword = 3,
        
        /// <summary>등록 비활성화됨</summary>
        RegistrationDisabled = 4
    }
}
