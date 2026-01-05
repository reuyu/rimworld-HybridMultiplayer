using System;
using HarmonyLib;
using RimWorld;
using Verse;
using HybridClient.Save;

namespace HybridClient.Patches
{
    /// <summary>
    /// 게임 저장 패치 - 저장 시 서버에 자동 업로드
    /// RT SavePatches 패턴 기반
    /// </summary>
    
    /// <summary>
    /// GameDataSaveLoader.SaveGame 저장 완료 후 서버에 업로드
    /// </summary>
    [HarmonyPatch(typeof(GameDataSaveLoader), "SaveGame", typeof(string))]
    public static class SaveGame_Patch
    {
        // 중복 업로드 방지
        private static bool isSaving = false;
        
        [HarmonyPostfix]
        public static void Postfix(string fileName)
        {
            // 연결 안됐으면 무시
            if (!NetworkManager.Instance?.IsConnected == true)
                return;
            
            // 중복 방지
            if (isSaving)
                return;
                
            try
            {
                isSaving = true;
                
                Log.Message($"[HybridMP][SAVE] Game saved: {fileName}, uploading to server...");
                
                // 비동기로 서버에 업로드
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        ClientSaveManager.Instance.UploadSavedFile(fileName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[HybridMP][SAVE] Upload failed: {ex.Message}");
                    }
                    finally
                    {
                        isSaving = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP][SAVE] Save patch error: {ex.Message}");
                isSaving = false;
            }
        }
    }
}
