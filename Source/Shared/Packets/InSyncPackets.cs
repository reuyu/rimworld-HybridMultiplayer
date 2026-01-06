using System;
using System.Collections.Generic;
using HybridShared;

namespace HybridShared.Packets
{
    // ========== InSync 패킷 열거형 ==========
    
    /// <summary>
    /// InSync 모드 종류
    /// </summary>
    public enum InSyncMode : byte
    {
        Battle = 0,     // 전투 (적대 상태)
        Coop = 1        // 협동 (비적대)
    }
    
    /// <summary>
    /// InSync 응답 종류
    /// </summary>
    public enum InSyncResponse : byte
    {
        Accepted = 0,
        Rejected = 1,
        Busy = 2        // 상대가 이미 다른 InSync 중
    }
    
    // ========== 진입 요청/응답 패킷 ==========
    
    /// <summary>
    /// A → 서버: 정착지 진입 요청
    /// </summary>
    public class InSyncRequestPacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncRequest;
        
        /// <summary>정착지 타일 ID</summary>
        public int TargetTileId { get; set; }
        
        /// <summary>정착지 소유자 유저네임</summary>
        public string TargetUsername { get; set; }
        
        /// <summary>모드 (전투/협동)</summary>
        public InSyncMode Mode { get; set; }
        
        /// <summary>침입할 폰 정보 목록</summary>
        public List<PawnInfo> Pawns { get; set; }
    }
    
    /// <summary>
    /// 동기화 대상 폰 정보
    /// </summary>
    public class PawnInfo
    {
        public string Name { get; set; }
        public string KindDef { get; set; }
        public string FactionDef { get; set; }
    }
    
    /// <summary>
    /// 서버 → B: 진입 알림
    /// </summary>
    public class InSyncNotifyPacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncNotify;
        
        /// <summary>요청자 유저네임</summary>
        public string RequesterUsername { get; set; }
        
        /// <summary>정착지 타일 ID</summary>
        public int TileId { get; set; }
        
        /// <summary>모드</summary>
        public InSyncMode Mode { get; set; }
        
        /// <summary>세션 ID</summary>
        public int SessionId { get; set; }
        
        /// <summary>침입할 폰 정보 목록</summary>
        public List<PawnInfo> Pawns { get; set; }
    }
    
    /// <summary>
    /// 서버 → A: 진입 응답
    /// </summary>
    public class InSyncResponsePacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncResponse;
        
        public InSyncResponse Response { get; set; }
        
        /// <summary>InSync 세션 ID (수락 시)</summary>
        public int SessionId { get; set; }
    }
    
    // ========== 맵 스냅샷 패킷 ==========
    
    /// <summary>
    /// B → 서버 → A: 맵 스냅샷 데이터
    /// </summary>
    public class MapSnapshotPacket : PacketBase
    {
        public override PacketType Type => PacketType.MapSnapshot;
        
        /// <summary>InSync 세션 ID</summary>
        public int SessionId { get; set; }
        
        /// <summary>맵 ID</summary>
        public int MapId { get; set; }
        
        /// <summary>압축된 맵 데이터 (GZip + Base64)</summary>
        public string CompressedMapDataBase64 { get; set; }
        
        /// <summary>현재 틱</summary>
        public int CurrentTick { get; set; }
        
        /// <summary>난수 상태</summary>
        public ulong RandState { get; set; }
        
        /// <summary>압축 전 원본 크기 (디버깅용)</summary>
        public int OriginalSize { get; set; }
        
        // Helper methods - GZip 압축 사용
        public byte[] GetCompressedMapData()
        {
            // Base64 디코딩 후 GZip 해제
            return InSyncCompression.DecodeAndDecompress(CompressedMapDataBase64);
        }
        
        public void SetCompressedMapData(byte[] data)
        {
            if (data == null)
            {
                CompressedMapDataBase64 = null;
                OriginalSize = 0;
                return;
            }
            
            OriginalSize = data.Length;
            // GZip 압축 후 Base64 인코딩
            CompressedMapDataBase64 = InSyncCompression.CompressAndEncode(data);
        }
    }
    
    // ========== Lockstep 동기화 패킷 ==========
    
    /// <summary>
    /// 틱 동기화 패킷
    /// </summary>
    public class LockstepTickPacket : PacketBase
    {
        public override PacketType Type => PacketType.LockstepTick;
        
        /// <summary>세션 ID</summary>
        public int SessionId { get; set; }
        
        /// <summary>현재 틱</summary>
        public int Tick { get; set; }
    }
    
    /// <summary>
    /// 명령 동기화 패킷 (MP ScheduledCommand 참조)
    /// </summary>
    public class LockstepCommandPacket : PacketBase
    {
        public override PacketType Type => PacketType.LockstepCommand;
        
        /// <summary>세션 ID</summary>
        public int SessionId { get; set; }
        
        /// <summary>실행할 틱</summary>
        public int ExecuteTick { get; set; }
        
        /// <summary>발신자 유저네임</summary>
        public string SenderUsername { get; set; }
        
        /// <summary>명령 종류</summary>
        public byte CommandType { get; set; }
        
        /// <summary>명령 데이터 (Base64)</summary>
        public string CommandDataBase64 { get; set; }
        
        // Helper methods
        public byte[] GetCommandData()
        {
            if (string.IsNullOrEmpty(CommandDataBase64))
                return null;
            return Convert.FromBase64String(CommandDataBase64);
        }
        
        public void SetCommandData(byte[] data)
        {
            CommandDataBase64 = data != null ? Convert.ToBase64String(data) : null;
        }
    }
    
    /// <summary>
    /// InSync 종료 패킷
    /// </summary>
    public class InSyncEndPacket : PacketBase
    {
        public override PacketType Type => PacketType.InSyncEnd;
        
        public int SessionId { get; set; }
        
        /// <summary>종료 사유</summary>
        public string Reason { get; set; }
    }
    
    /// <summary>
    /// Desync 감지용 동기화 상태 패킷
    /// MP ClientSyncOpinion 패턴
    /// </summary>
    public class SyncOpinionPacket : PacketBase
    {
        public override PacketType Type => PacketType.SyncOpinion;
        
        public int SessionId { get; set; }
        
        /// <summary>Opinion 시작 틱</summary>
        public int StartTick { get; set; }
        
        /// <summary>틱별 랜덤 상태 해시</summary>
        public List<uint> TickStates { get; set; } = new List<uint>();
        
        /// <summary>명령별 랜덤 상태 해시</summary>
        public List<uint> CommandStates { get; set; } = new List<uint>();
    }
}
