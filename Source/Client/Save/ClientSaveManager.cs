using System;
using System.IO;
using System.Threading.Tasks;
using HybridShared.Packets;
using RimWorld;
using Verse;

namespace HybridClient.Save
{
    /// <summary>
    /// 클라이언트 세이브 매니저 - RT SaveManager 패턴 기반
    /// 세이브 저장 시 서버에 업로드, 재접속 시 서버에서 다운로드
    /// </summary>
    public class ClientSaveManager
    {
        private static ClientSaveManager _instance;
        public static ClientSaveManager Instance => _instance ??= new ClientSaveManager();
        
        // 서버에서 받은 세이브 데이터
        private byte[] pendingSaveData;
        private string pendingSaveName;
        
        // 마지막으로 저장된 파일명
        public string LastSavedFileName { get; private set; }
        
        // 서버용 고정 세이브 이름
        public string ServerSaveName => $"MP_{NetworkManager.Instance?.Username ?? "Player"}";
        
        // 세이브 로드 이벤트
        public event Action<byte[]> OnSaveReceived;
        
        private ClientSaveManager()
        {
            Log.Message("[HybridMP][SAVE] ClientSaveManager initialized");
        }
        
        /// <summary>
        /// 저장된 파일을 서버에 업로드 (SavePatch에서 호출)
        /// </summary>
        public void UploadSavedFile(string fileName)
        {
            if (!NetworkManager.Instance?.IsConnected == true)
            {
                Log.Warning("[HybridMP][SAVE] Not connected to server");
                return;
            }
            
            LastSavedFileName = fileName;
            
            Task.Run(() =>
            {
                try
                {
                    // 저장 완료 대기
                    System.Threading.Thread.Sleep(500);
                    
                    string savePath = Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", fileName + ".rws");
                    
                    if (File.Exists(savePath))
                    {
                        byte[] saveData = File.ReadAllBytes(savePath);
                        byte[] compressed = PacketSerializer.Compress(saveData);
                        
                        var packet = new SaveUploadPacket
                        {
                            SaveData = compressed,
                            SaveName = ServerSaveName, // 서버에서는 username 기반 이름 사용
                            GameTicks = Find.TickManager?.TicksGame ?? 0
                        };
                        
                        NetworkManager.Instance.Send(packet);
                        Log.Message($"[HybridMP][SAVE] Uploaded save to server ({compressed.Length} bytes)");
                    }
                    else
                    {
                        Log.Error($"[HybridMP][SAVE] Save file not found: {savePath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[HybridMP][SAVE] Failed to upload save: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// 서버에 세이브 요청
        /// </summary>
        public void RequestSaveFromServer()
        {
            if (!NetworkManager.Instance?.IsConnected == true)
            {
                Log.Warning("[HybridMP][SAVE] Not connected to server");
                return;
            }
            
            Log.Message("[HybridMP][SAVE] Requesting save from server...");
            NetworkManager.Instance.Send(new SaveRequestPacket());
        }
        
        /// <summary>
        /// 서버에서 세이브 수신 핸들러
        /// </summary>
        public void HandleSaveDownload(SaveDownloadPacket packet)
        {
            if (!packet.HasSave)
            {
                Log.Message("[HybridMP][SAVE] No save found on server - starting new game");
                pendingSaveData = null;
                pendingSaveName = null;
                OnSaveReceived?.Invoke(null);
                return;
            }
            
            Log.Message($"[HybridMP][SAVE] Received save from server ({packet.SaveData?.Length ?? 0} bytes)");
            
            try
            {
                // 압축 해제
                byte[] decompressed = PacketSerializer.Decompress(packet.SaveData);
                pendingSaveData = decompressed;
                
                // 로컬에 저장할 파일명 (서버 세이브 이름 사용)
                pendingSaveName = ServerSaveName;
                
                // 세이브 파일로 저장
                string savePath = Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", pendingSaveName + ".rws");
                File.WriteAllBytes(savePath, decompressed);
                
                Log.Message($"[HybridMP][SAVE] Save stored locally: {savePath}");
                
                // 이벤트 호출 (저장 완료 후)
                OnSaveReceived?.Invoke(decompressed);
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP][SAVE] Failed to process save: {ex.Message}");
                OnSaveReceived?.Invoke(null);
            }
        }
        
        /// <summary>
        /// 저장된 세이브가 있는지 확인
        /// </summary>
        public bool HasPendingSave => pendingSaveData != null && pendingSaveData.Length > 0;
        
        /// <summary>
        /// 저장된 세이브 로드
        /// </summary>
        public void LoadPendingSave()
        {
            if (!HasPendingSave || string.IsNullOrEmpty(pendingSaveName))
            {
                Log.Warning("[HybridMP][SAVE] No pending save to load");
                return;
            }
            
            Log.Message($"[HybridMP][SAVE] Loading save: {pendingSaveName}");
            GameDataSaveLoader.LoadGame(pendingSaveName);
            
            // 로드 후 클리어
            pendingSaveData = null;
            pendingSaveName = null;
        }
        
        /// <summary>
        /// 수동 세이브 강제 실행 및 업로드
        /// </summary>
        public void ForceSaveAndUpload()
        {
            Log.Message("[HybridMP][SAVE] Force saving and uploading...");
            string saveName = ServerSaveName;
            GameDataSaveLoader.SaveGame(saveName);
            // SavePatch가 자동으로 업로드 처리
        }
    }
}

