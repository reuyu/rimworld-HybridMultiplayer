using System;

namespace HybridShared.Packets
{
    /// <summary>
    /// 세이브 관련 패킷 - RT SaveManager 패턴 기반
    /// </summary>
    
    /// <summary>
    /// 세이브 업로드 (클라이언트 → 서버)
    /// </summary>
    public class SaveUploadPacket : PacketBase
    {
        public override PacketType Type => PacketType.SaveUpload;
        
        /// <summary>압축된 세이브 데이터</summary>
        public byte[] SaveData { get; set; }
        
        /// <summary>세이브 이름</summary>
        public string SaveName { get; set; }
        
        /// <summary>게임 내 틱</summary>
        public int GameTicks { get; set; }
    }
    
    /// <summary>
    /// 세이브 다운로드 (서버 → 클라이언트)
    /// </summary>
    public class SaveDownloadPacket : PacketBase
    {
        public override PacketType Type => PacketType.SaveDownload;
        
        /// <summary>압축된 세이브 데이터</summary>
        public byte[] SaveData { get; set; }
        
        /// <summary>세이브 이름</summary>
        public string SaveName { get; set; }
        
        /// <summary>세이브가 존재하는지 여부</summary>
        public bool HasSave { get; set; }
        
        /// <summary>메시지</summary>
        public string Message { get; set; }
    }
    
    /// <summary>
    /// 세이브 요청 (클라이언트 → 서버)
    /// </summary>
    public class SaveRequestPacket : PacketBase
    {
        public override PacketType Type => PacketType.SaveRequest;
    }
}
