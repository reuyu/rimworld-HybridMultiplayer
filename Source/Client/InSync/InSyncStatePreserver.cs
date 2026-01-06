using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace HybridClient.InSync
{
    /// <summary>
    /// InSync 진입/종료 시 게임 상태 보존
    /// MP SaveLoad 패턴 적용
    /// </summary>
    public static class InSyncStatePreserver
    {
        // ===== 선택 상태 보존 =====
        private static List<object> savedSelection = new List<object>();
        
        // ===== tweenedPos 보존 =====
        private static Dictionary<int, Vector3> savedTweenedPos = new Dictionary<int, Vector3>();
        
        // ===== 카메라 상태 보존 =====
        private static Vector3 savedCameraPos;
        private static float savedCameraSize;
        
        /// <summary>
        /// InSync 진입 전 상태 저장
        /// </summary>
        public static void SaveState(Map map)
        {
            Log.Message("[HybridMP][STATE] Saving state before InSync...");
            
            // 1. 선택 상태 저장
            SaveSelection();
            
            // 2. tweenedPos 저장
            SaveTweenedPositions(map);
            
            // 3. 카메라 상태 저장
            SaveCameraState();
            
            Log.Message($"[HybridMP][STATE] Saved: {savedSelection.Count} selected, {savedTweenedPos.Count} tweenedPos");
        }
        
        /// <summary>
        /// InSync 맵 로드 후 상태 복원
        /// </summary>
        public static void RestoreState(Map map)
        {
            Log.Message("[HybridMP][STATE] Restoring state after InSync map load...");
            
            // 1. tweenedPos 복원
            RestoreTweenedPositions(map);
            
            // 2. 선택 상태 복원
            RestoreSelection();
            
            // 3. 카메라 상태 복원
            RestoreCameraState();
            
            Log.Message("[HybridMP][STATE] State restored");
        }
        
        /// <summary>
        /// InSync 종료 시 정리
        /// </summary>
        public static void Cleanup()
        {
            savedSelection.Clear();
            savedTweenedPos.Clear();
            Log.Message("[HybridMP][STATE] State cleared");
        }
        
        // ===== 선택 상태 =====
        
        private static void SaveSelection()
        {
            savedSelection.Clear();
            
            if (Find.Selector?.SelectedObjects != null)
            {
                foreach (var obj in Find.Selector.SelectedObjects)
                {
                    savedSelection.Add(obj);
                }
            }
        }
        
        private static void RestoreSelection()
        {
            if (savedSelection.Count == 0)
                return;
            
            Find.Selector?.ClearSelection();
            
            foreach (var obj in savedSelection)
            {
                // 오브젝트가 여전히 유효한지 확인
                if (obj is Thing thing && thing.Spawned)
                {
                    Find.Selector?.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }
                else if (obj is Zone zone && zone.Map != null)
                {
                    Find.Selector?.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }
            }
        }
        
        // ===== tweenedPos =====
        
        private static void SaveTweenedPositions(Map map)
        {
            savedTweenedPos.Clear();
            
            if (map?.mapPawns?.AllPawnsSpawned == null)
                return;
            
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn?.Drawer?.tweener != null)
                {
                    savedTweenedPos[pawn.thingIDNumber] = pawn.Drawer.tweener.TweenedPos;
                }
            }
        }
        
        private static void RestoreTweenedPositions(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
                return;
            
            int restored = 0;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn?.Drawer?.tweener == null)
                    continue;
                
                if (savedTweenedPos.TryGetValue(pawn.thingIDNumber, out Vector3 pos))
                {
                    // tweener는 내부적으로 관리되므로 위치만 설정
                    // TweenedPos 프로퍼티가 get only인 경우 대안 사용
                    try
                    {
                        var tweener = pawn.Drawer.tweener;
                        // Reflection으로 tweenedPos 직접 설정
                        var field = HarmonyLib.AccessTools.Field(typeof(PawnTweener), "tweenedPos");
                        if (field != null)
                        {
                            field.SetValue(tweener, pos);
                            restored++;
                        }
                    }
                    catch
                    {
                        // 무시 - 호환성 문제
                    }
                }
            }
            
            Log.Message($"[HybridMP][STATE] Restored {restored} tweenedPos");
        }
        
        // ===== 카메라 상태 =====
        
        private static void SaveCameraState()
        {
            // CameraDriver의 위치 정보 저장 - 기본 방식
            try
            {
                if (Find.CameraDriver?.transform != null)
                {
                    savedCameraPos = Find.CameraDriver.transform.position;
                }
            }
            catch
            {
                // 무시
            }
        }
        
        private static void RestoreCameraState()
        {
            // 카메라 위치 복원
            try
            {
                if (Find.CameraDriver != null && savedCameraPos != Vector3.zero)
                {
                    Find.CameraDriver.JumpToCurrentMapLoc(savedCameraPos);
                }
            }
            catch
            {
                // 무시
            }
        }
    }
}

