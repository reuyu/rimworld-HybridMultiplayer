using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HybridClient.InSync
{
    /// <summary>
    /// InSync 세력 관리자
    /// MP FactionContext 패턴 적용 - 세력 컨텍스트 Push/Pop
    /// </summary>
    public static class InSyncFactionManager
    {
        private static Stack<Faction> factionStack = new Stack<Faction>();
        private static Dictionary<string, Faction> playerFactions = new Dictionary<string, Faction>();
        
        /// <summary>현재 활성 플레이어의 세력</summary>
        public static Faction MyFaction { get; private set; }
        
        /// <summary>침입자 세력 (InSync 중에만 유효)</summary>
        public static Faction InvaderFaction { get; private set; }
        
        /// <summary>권위자 세력 (InSync 중에만 유효)</summary>
        public static Faction AuthorityFaction { get; private set; }
        
        /// <summary>
        /// 세력 컨텍스트 Push - 현재 세력 저장 후 새 세력으로 전환
        /// MP FactionContext.Push 참조
        /// </summary>
        public static Faction Push(Faction newFaction, bool force = false)
        {
            if (newFaction == null || !force && Faction.OfPlayer == newFaction || !newFaction.def.isPlayer)
            {
                factionStack.Push(null);
                return null;
            }
            
            factionStack.Push(Find.FactionManager.OfPlayer);
            Set(newFaction);
            
            return newFaction;
        }
        
        /// <summary>
        /// 세력 컨텍스트 Pop - 이전 세력으로 복원
        /// MP FactionContext.Pop 참조
        /// </summary>
        public static Faction Pop()
        {
            if (factionStack.Count == 0)
            {
                Log.Warning("[HybridMP][FACTION] Faction stack is empty");
                return null;
            }
            
            Faction f = factionStack.Pop();
            if (f != null)
                Set(f);
            return f;
        }
        
        /// <summary>
        /// 현재 플레이어 세력 설정
        /// </summary>
        public static void Set(Faction newFaction)
        {
            // RimWorld에서 직접 ofPlayer 설정 불가 - 내부 상태로만 관리
            // Find.FactionManager.ofPlayer = newFaction;
        }
        
        /// <summary>
        /// 세력 스택 초기화
        /// </summary>
        public static void Clear()
        {
            factionStack.Clear();
        }
        
        /// <summary>
        /// 침입자용 별도 플레이어 세력 생성
        /// MP FactionCreator.NewFactionWithIdeo 패턴 참조
        /// </summary>
        public static Faction CreateInvaderFaction(string username)
        {
            try
            {
                // 이미 존재하면 반환
                if (playerFactions.TryGetValue(username, out var existingFaction))
                {
                    Log.Message($"[HybridMP][FACTION] Reusing existing faction for {username}");
                    return existingFaction;
                }
                
                // 새 플레이어 세력 생성
                var factionDef = FactionDefOf.PlayerColony;
                var faction = new Faction();
                faction.def = factionDef;
                faction.loadID = Find.UniqueIDsManager.GetNextFactionID();
                faction.Name = $"{username}'s Forces";
                faction.colorFromSpectrum = UnityEngine.Random.Range(0f, 1f);
                
                // FactionManager에 추가 (relations 자동 초기화)
                Find.FactionManager.Add(faction);
                
                // 모든 맵의 attackTargetsCache 알림 (MP 패턴)
                foreach (Map map in Find.Maps)
                {
                    foreach (var f in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
                    {
                        map.attackTargetsCache.Notify_FactionHostilityChanged(f, faction);
                    }
                }
                
                playerFactions[username] = faction;
                
                Log.Message($"[HybridMP][FACTION] Created invader faction: {faction.Name} (ID: {faction.loadID})");
                return faction;
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][FACTION] Failed to create invader faction: {e}");
                return Faction.OfPlayer;
            }
        }
        
        /// <summary>
        /// InSync 시작 시 초기화
        /// </summary>
        public static void InitializeForInSync(bool isAuthority, string partnerUsername)
        {
            Clear();
            
            if (isAuthority)
            {
                // 권위자: 기존 플레이어 세력 유지
                AuthorityFaction = Faction.OfPlayer;
                InvaderFaction = CreateInvaderFaction(partnerUsername);
                MyFaction = AuthorityFaction;
            }
            else
            {
                // 침입자: 별도 세력 생성 (게임 로드 후 설정)
                AuthorityFaction = Faction.OfPlayer;
                InvaderFaction = null; // 게임 로드 후 설정
                MyFaction = null; // 게임 로드 후 설정
            }
            
            Log.Message($"[HybridMP][FACTION] Initialized for InSync - IsAuthority: {isAuthority}, Authority: {AuthorityFaction?.Name}, Invader: {InvaderFaction?.Name}");
        }
        
        /// <summary>
        /// 침입자 게임 로드 후 세력 설정
        /// </summary>
        public static void SetupInvaderFactionAfterLoad(string myUsername)
        {
            // 침입자용 세력 생성
            InvaderFaction = CreateInvaderFaction(myUsername);
            MyFaction = InvaderFaction;
            
            Log.Message($"[HybridMP][FACTION] Invader faction setup complete: {InvaderFaction?.Name}");
        }
        
        /// <summary>
        /// 폰이 현재 플레이어 소유인지 확인
        /// </summary>
        public static bool IsMyPawn(Pawn pawn)
        {
            if (pawn == null || MyFaction == null)
                return false;
            
            return pawn.Faction == MyFaction;
        }
        
        /// <summary>
        /// InSync 종료 시 정리
        /// </summary>
        public static void Cleanup()
        {
            Clear();
            MyFaction = null;
            AuthorityFaction = null;
            InvaderFaction = null;
            
            Log.Message("[HybridMP][FACTION] Faction manager cleaned up");
        }
    }
}
