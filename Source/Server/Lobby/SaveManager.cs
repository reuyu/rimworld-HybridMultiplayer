using System;
using System.IO;
using HybridShared;
using HybridShared.Packets;

namespace HybridServer.Lobby
{
    /// <summary>
    /// 서버 세이브 매니저 - 유저별 세이브 파일 관리
    /// RT SaveManager 패턴 기반
    /// </summary>
    public class SaveManager
    {
        private static SaveManager _instance;
        public static SaveManager Instance => _instance ??= new SaveManager();
        
        private readonly string savesDirectory;
        
        private SaveManager()
        {
            savesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "saves");
            
            if (!Directory.Exists(savesDirectory))
            {
                Directory.CreateDirectory(savesDirectory);
            }
            
            HybridLogger.Log(LogCategory.Lobby, "SaveManager initialized");
        }
        
        /// <summary>
        /// 유저별 세이브 파일 경로
        /// </summary>
        public string GetSaveFilePath(string username)
        {
            // 유효하지 않은 문자 제거
            string safeName = string.Join("_", username.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(savesDirectory, $"{safeName}.rws");
        }
        
        /// <summary>
        /// 세이브 파일 존재 여부 확인
        /// </summary>
        public bool HasSave(string username)
        {
            return File.Exists(GetSaveFilePath(username));
        }
        
        /// <summary>
        /// 세이브 파일 저장
        /// </summary>
        public void SaveUserData(string username, byte[] saveData)
        {
            try
            {
                string filePath = GetSaveFilePath(username);
                File.WriteAllBytes(filePath, saveData);
                HybridLogger.Log(LogCategory.Lobby, $"Save stored for {username}: {saveData.Length} bytes");
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Lobby, $"Failed to save data for {username}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 세이브 파일 로드
        /// </summary>
        public byte[] LoadUserData(string username)
        {
            try
            {
                string filePath = GetSaveFilePath(username);
                if (File.Exists(filePath))
                {
                    var data = File.ReadAllBytes(filePath);
                    HybridLogger.Log(LogCategory.Lobby, $"Save loaded for {username}: {data.Length} bytes");
                    return data;
                }
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Lobby, $"Failed to load data for {username}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// 세이브 업로드 핸들러
        /// </summary>
        public void HandleSaveUpload(string username, SaveUploadPacket packet)
        {
            if (packet.SaveData == null || packet.SaveData.Length == 0)
            {
                HybridLogger.Warn(LogCategory.Lobby, $"Received empty save from {username}");
                return;
            }
            
            SaveUserData(username, packet.SaveData);
        }
        
        /// <summary>
        /// 세이브 다운로드 패킷 생성
        /// </summary>
        public SaveDownloadPacket CreateSaveDownloadPacket(string username)
        {
            var saveData = LoadUserData(username);
            
            if (saveData != null)
            {
                return new SaveDownloadPacket
                {
                    HasSave = true,
                    SaveData = saveData,
                    SaveName = $"{username}_save",
                    Message = "Save loaded successfully"
                };
            }
            else
            {
                return new SaveDownloadPacket
                {
                    HasSave = false,
                    SaveData = null,
                    SaveName = null,
                    Message = "No save found for this user"
                };
            }
        }
    }
}
