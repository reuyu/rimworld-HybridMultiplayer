using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HybridClient.Patches
{
    /// <summary>
    /// 메인 메뉴 패치 - Multiplayer 버튼 추가
    /// RT MainMenuPatches.cs 기반 - HybridMP 적응
    /// </summary>
    
    /// <summary>
    /// 버전 정보 표시
    /// </summary>
    [HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
    public static class VersionControl_Patch
    {
        public static void Postfix()
        {
            string toDisplay = "Hybrid Multiplayer v0.2";
            Vector2 size = Text.CalcSize(toDisplay);
            Rect rect = new Rect(10f, 73f, size.x, size.y);
            
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(rect, toDisplay);
            GUI.color = Color.white;
            
            // 메인 메뉴에서도 네트워크 업데이트
            NetworkManager.Instance?.Update();
        }
    }
    
    /// <summary>
    /// 메인 메뉴에 "Multiplayer" 버튼 추가
    /// </summary>
    [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
    public static class MainMenuOptionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Rect rect, List<ListableOption> optList)
        {
            // 메인 메뉴 화면에서만 실행
            if (Current.ProgramState != ProgramState.Entry) return true;
            
            // 첫 번째 옵션이 ListableOption일 때 (기본 메뉴)
            if (optList.Count > 0 && optList[0].GetType() == typeof(ListableOption))
            {
                // "Multiplayer" 버튼을 맨 위에 추가
                optList.Insert(0, new ListableOption("Multiplayer", delegate
                {
                    // 연결 다이얼로그 표시
                    ShowConnectDialog();
                }));
            }
            
            return true;
        }
        
        private static void ShowConnectDialog()
        {
            // 연결 다이얼로그 표시
            Find.WindowStack.Add(new Dialog_ConnectToServer());
        }
    }
    
    /// <summary>
    /// 서버 연결 다이얼로그
    /// </summary>
    public class Dialog_ConnectToServer : Window
    {
        private string serverIp = "127.0.0.1";
        private string serverPort = "30000";
        private string username = "Player";
        private string password = "";
        
        public Dialog_ConnectToServer()
        {
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }
        
        public override Vector2 InitialSize => new Vector2(400f, 320f);
        
        public override void DoWindowContents(Rect inRect)
        {
            // 네트워크 업데이트 호출
            NetworkManager.Instance?.Update();
            
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 40f), "Connect to Server");
            Text.Font = GameFont.Small;
            
            float y = 50f;
            float labelWidth = 100f;
            float fieldWidth = inRect.width - labelWidth - 10f;
            float rowHeight = 30f;
            
            // Server IP
            Widgets.Label(new Rect(0f, y, labelWidth, rowHeight), "Server IP:");
            serverIp = Widgets.TextField(new Rect(labelWidth, y, fieldWidth, rowHeight), serverIp);
            y += rowHeight + 5f;
            
            // Port
            Widgets.Label(new Rect(0f, y, labelWidth, rowHeight), "Port:");
            serverPort = Widgets.TextField(new Rect(labelWidth, y, fieldWidth, rowHeight), serverPort);
            y += rowHeight + 5f;
            
            // Username
            Widgets.Label(new Rect(0f, y, labelWidth, rowHeight), "Username:");
            username = Widgets.TextField(new Rect(labelWidth, y, fieldWidth, rowHeight), username);
            y += rowHeight + 5f;
            
            // Password (일반 텍스트필드 사용, 마스킹은 나중에 구현)
            Widgets.Label(new Rect(0f, y, labelWidth, rowHeight), "Password:");
            password = Widgets.TextField(new Rect(labelWidth, y, fieldWidth, rowHeight), password);
            y += rowHeight + 20f;
            
            // Connect button
            float buttonWidth = 120f;
            float buttonX = (inRect.width - buttonWidth * 2 - 10f) / 2f;
            
            if (Widgets.ButtonText(new Rect(buttonX, y, buttonWidth, 35f), "Connect"))
            {
                TryConnect();
            }
            
            if (Widgets.ButtonText(new Rect(buttonX + buttonWidth + 10f, y, buttonWidth, 35f), "Cancel"))
            {
                Close();
            }
            
            y += 50f;
            
            // Status
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            var state = NetworkManager.Instance?.State ?? NetworkState.Disconnected;
            Widgets.Label(new Rect(0f, y, inRect.width, 20f), $"Status: {state}");
            GUI.color = Color.white;
        }
        
        private void TryConnect()
        {
            if (!int.TryParse(serverPort, out int port))
            {
                Messages.Message("Invalid port number", MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (string.IsNullOrEmpty(username))
            {
                Messages.Message("Username is required", MessageTypeDefOf.RejectInput);
                return;
            }
            
            Log.Message($"[HybridMP] Connecting to {serverIp}:{port} as {username}...");
            
            try
            {
                // 네트워크 매니저로 연결
                NetworkManager.Instance.Connect(serverIp, port, username);
                
                Messages.Message($"Connecting to {serverIp}:{port}...", MessageTypeDefOf.PositiveEvent);
                // 다이얼로그를 닫지 않고 연결 상태 확인
                // Close();
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] Connection failed: {ex.Message}");
                Messages.Message($"Connection failed: {ex.Message}", MessageTypeDefOf.RejectInput);
            }
        }
    }
    
    /// <summary>
    /// 게임 루프에서 네트워크 업데이트 호출
    /// </summary>
    [HarmonyPatch(typeof(Root), "Update")]
    public static class Root_Update_Patch
    {
        public static void Postfix()
        {
            // 항상 네트워크 업데이트 실행
            NetworkManager.Instance?.Update();
        }
    }
}
