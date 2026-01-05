using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using HybridClient.Save;

namespace HybridClient.Patches
{
    /// <summary>
    /// ESC 메뉴 패치 - 저장 후 로그아웃 버튼 추가
    /// RT EscMenuPatches 패턴 기반 (Prefix + Postfix)
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), "DoMainMenuControls")]
    public static class EscMenu_SaveAndLogout_Patch
    {
        private static bool isSavingAndLoggingOut = false;
        
        // Prefix: 투명 버튼으로 클릭 감지
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // 연결 중이고 게임 플레이 중일 때만
            if (NetworkManager.Instance?.IsConnected != true)
                return true;
            if (Current.ProgramState != ProgramState.Playing)
                return true;
            
            // 버튼 크기 및 위치 (RT 스타일 - 기존 버튼 아래)
            Vector2 buttonSize = new Vector2(170f, 45f);
            float yOffset = (buttonSize.y + 7f) * 5f; // 5번째 위치
            
            // 투명 버튼으로 클릭 감지
            if (Widgets.ButtonText(new Rect(0, yOffset, buttonSize.x, buttonSize.y), ""))
            {
                if (!isSavingAndLoggingOut)
                {
                    SaveAndLogout();
                }
            }
            
            return true;
        }
        
        // Postfix: 버튼 텍스트 그리기
        [HarmonyPostfix]
        public static void Postfix()
        {
            // 연결 중이고 게임 플레이 중일 때만
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            if (Current.ProgramState != ProgramState.Playing)
                return;
            
            // 버튼 크기 및 위치
            Vector2 buttonSize = new Vector2(170f, 45f);
            float yOffset = (buttonSize.y + 7f) * 5f;
            
            // 버튼 텍스트만 그리기
            GUI.color = new Color(0.5f, 0.8f, 1f); // 밝은 파랑색
            if (Widgets.ButtonText(new Rect(0, yOffset, buttonSize.x, buttonSize.y), "저장 후 로그아웃")) { }
            GUI.color = Color.white;
        }
        
        private static void SaveAndLogout()
        {
            isSavingAndLoggingOut = true;
            
            try
            {
                Log.Message("[HybridMP] Save and logout requested");
                
                // ESC 메뉴 닫기
                Find.MainTabsRoot?.EscapeCurrentTab(playSound: false);
                
                // 세이브 강제 실행 및 업로드
                ClientSaveManager.Instance.ForceSaveAndUpload();
                
                // 저장 완료 대기 후 연결 해제 및 메인 메뉴로
                System.Threading.Tasks.Task.Run(() =>
                {
                    // 저장 및 업로드 대기
                    System.Threading.Thread.Sleep(2000);
                    
                    // 메인 스레드에서 실행
                    LongEventHandler.QueueLongEvent(() =>
                    {
                        try
                        {
                            // 서버 연결 해제
                            NetworkManager.Instance?.Disconnect();
                            
                            // 메인 메뉴로 이동
                            GenScene.GoToMainMenu();
                            
                            Log.Message("[HybridMP] Logged out successfully");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[HybridMP] Logout error: {ex.Message}");
                        }
                        finally
                        {
                            isSavingAndLoggingOut = false;
                        }
                    }, "LoggingOut", doAsynchronously: false, null);
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] Save and logout failed: {ex.Message}");
                isSavingAndLoggingOut = false;
            }
        }
    }
}
