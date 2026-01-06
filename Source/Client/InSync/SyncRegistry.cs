using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;
using HarmonyLib;

namespace HybridClient.InSync
{
    /// <summary>
    /// 동기화 메서드 등록 어트리뷰트
    /// MP ISyncMethod 패턴 적용
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class InSyncMethodAttribute : Attribute
    {
        public string MethodName { get; }
        public Type TargetType { get; }
        
        public InSyncMethodAttribute(Type targetType, string methodName)
        {
            TargetType = targetType;
            MethodName = methodName;
        }
    }
    
    /// <summary>
    /// 동기화 필드 등록 어트리뷰트
    /// MP ISyncField 패턴 적용
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class InSyncFieldAttribute : Attribute
    {
        public string FieldPath { get; }
        
        public InSyncFieldAttribute(string fieldPath = null)
        {
            FieldPath = fieldPath;
        }
    }
    
    /// <summary>
    /// 메서드 동기화 정보
    /// </summary>
    public class SyncMethodInfo
    {
        public int Id { get; set; }
        public Type TargetType { get; set; }
        public string MethodName { get; set; }
        public MethodInfo Method { get; set; }
        
        public void Invoke(object target, object[] args)
        {
            Method?.Invoke(target, args);
        }
    }
    
    /// <summary>
    /// 동기화 메서드/필드 레지스트리
    /// MP SyncDict 패턴 적용
    /// </summary>
    public static class SyncRegistry
    {
        private static Dictionary<int, SyncMethodInfo> methods = new Dictionary<int, SyncMethodInfo>();
        private static Dictionary<string, int> methodNameToId = new Dictionary<string, int>();
        private static int nextMethodId = 100;
        
        public static bool IsInitialized { get; private set; }
        
        /// <summary>
        /// 동기화 시스템 초기화
        /// </summary>
        public static void Initialize()
        {
            if (IsInitialized) return;
            
            // 핵심 메서드 등록
            RegisterCoreCommands();
            
            IsInitialized = true;
            Log.Message($"[HybridMP][SYNC] Registry initialized with {methods.Count} methods");
        }
        
        /// <summary>
        /// 핵심 명령어 등록
        /// </summary>
        private static void RegisterCoreCommands()
        {
            // Pawn 관련
            RegisterMethod<Pawn_DraftController>("Drafted", typeof(bool));
            RegisterMethod<Verse.AI.Pawn_JobTracker>("StartJob");
            RegisterMethod<Verse.AI.Pawn_JobTracker>("StopAll");
            
            // Designator 관련 (건설, 지정 등)
            RegisterMethod<Designator>("DesignateSingleCell");
            RegisterMethod<Designator>("DesignateThing");
            
            // Gizmo 관련
            RegisterMethod<Command>("ProcessInput");
            RegisterMethod<Command_Toggle>("ToggleAction");
        }
        
        /// <summary>
        /// 메서드 등록
        /// </summary>
        public static int RegisterMethod<T>(string methodName, params Type[] paramTypes)
        {
            var type = typeof(T);
            var key = $"{type.FullName}.{methodName}";
            
            if (methodNameToId.TryGetValue(key, out int existingId))
                return existingId;
            
            var method = AccessTools.Method(type, methodName, paramTypes);
            if (method == null)
            {
                // 파라미터 없이 다시 시도
                method = AccessTools.Method(type, methodName);
            }
            
            var id = nextMethodId++;
            var info = new SyncMethodInfo
            {
                Id = id,
                TargetType = type,
                MethodName = methodName,
                Method = method
            };
            
            methods[id] = info;
            methodNameToId[key] = id;
            
            Log.Message($"[HybridMP][SYNC] Registered method {id}: {key}");
            return id;
        }
        
        /// <summary>
        /// ID로 메서드 정보 가져오기
        /// </summary>
        public static SyncMethodInfo GetMethod(int id)
        {
            return methods.TryGetValue(id, out var info) ? info : null;
        }
        
        /// <summary>
        /// 타입+이름으로 메서드 ID 가져오기
        /// </summary>
        public static int GetMethodId(Type type, string methodName)
        {
            var key = $"{type.FullName}.{methodName}";
            return methodNameToId.TryGetValue(key, out int id) ? id : -1;
        }
    }
    
    /// <summary>
    /// Gizmo 입력 패치 (MP GizmoPatch 패턴)
    /// 모든 UI 버튼 입력을 캡처하여 동기화
    /// </summary>
    [HarmonyPatch]
    public static class GizmoSyncPatches
    {
        /// <summary>
        /// Command.ProcessInput 패치 - 버튼 클릭 동기화
        /// </summary>
        [HarmonyPatch(typeof(Command), nameof(Command.ProcessInput))]
        [HarmonyPrefix]
        public static bool ProcessInput_Prefix(Command __instance, Event ev)
        {
            // InSync 아닐 때는 원래대로
            if (!SyncHandler.Instance.IsCapturing || !InSyncManager.Instance.IsActive)
                return true;
            
            // 동기화 패킷 전송
            SyncHandler.Instance.SyncGizmoPress(__instance);
            
            // 원래 메서드 실행 (로컬 효과)
            return true;
        }
    }
    
    /// <summary>
    /// Designator 패치 (MP Designator 패턴)
    /// 건설/지정 명령 동기화
    /// </summary>
    [HarmonyPatch]
    public static class DesignatorSyncPatches
    {
        /// <summary>
        /// DesignateSingleCell 패치 - 셀 지정 동기화
        /// </summary>
        [HarmonyPatch(typeof(Designator), nameof(Designator.DesignateSingleCell))]
        [HarmonyPrefix]
        public static bool DesignateSingleCell_Prefix(Designator __instance, IntVec3 c)
        {
            if (!SyncHandler.Instance.IsCapturing || !InSyncManager.Instance.IsActive)
                return true;
            
            // 동기화 패킷 전송
            SyncHandler.Instance.SyncDesignation(__instance, c);
            
            return true;
        }
        
        /// <summary>
        /// DesignateThing 패치 - Thing 지정 동기화
        /// </summary>
        [HarmonyPatch(typeof(Designator), nameof(Designator.DesignateThing))]
        [HarmonyPrefix]
        public static bool DesignateThing_Prefix(Designator __instance, Thing t)
        {
            if (!SyncHandler.Instance.IsCapturing || !InSyncManager.Instance.IsActive)
                return true;
            
            // 동기화 패킷 전송
            SyncHandler.Instance.SyncDesignation(__instance, t);
            
            return true;
        }
    }
    
    /// <summary>
    /// FloatMenu 패치 (MP FloatMenu 패턴)
    /// 우클릭 메뉴 명령 동기화
    /// </summary>
    [HarmonyPatch]
    public static class FloatMenuSyncPatches
    {
        /// <summary>
        /// FloatMenuOption 실행 패치
        /// </summary>
        [HarmonyPatch(typeof(FloatMenuOption), nameof(FloatMenuOption.Chosen))]
        [HarmonyPrefix]
        public static bool Chosen_Prefix(FloatMenuOption __instance, bool colonistOrdering, FloatMenu floatMenu)
        {
            if (!SyncHandler.Instance.IsCapturing || !InSyncManager.Instance.IsActive)
                return true;
            
            // FloatMenu 선택 동기화
            SyncHandler.Instance.SyncFloatMenuChoice(__instance);
            
            return true;
        }
    }
}
