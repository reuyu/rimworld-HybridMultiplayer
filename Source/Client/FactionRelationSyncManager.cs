using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using HybridShared.Packets;

namespace HybridClient
{
    /// <summary>
    /// 세력 관계 동기화 관리자
    /// - 서버에서 관계 요청/수신
    /// - 로컬 Faction에 관계 적용
    /// - 관계 변경 시 서버에 동기화
    /// </summary>
    public static class FactionRelationSyncManager
    {
        private static bool initialized = false;
        
        /// <summary>
        /// 서버에서 모든 관계 요청 (접속 시 호출)
        /// </summary>
        public static void RequestAllRelations()
        {
            var packet = new FactionRelationsRequestPacket
            {
                Username = NetworkManager.Instance?.Username ?? "Unknown"
            };
            
            NetworkManager.Instance?.Send(packet);
            Log.Message("[HybridMP][FACTION] Requested faction relations from server");
        }
        
        /// <summary>
        /// 서버에서 받은 관계 적용
        /// </summary>
        public static void ApplyRelations(List<FactionRelationData> relations)
        {
            string myUsername = NetworkManager.Instance?.Username;
            if (string.IsNullOrEmpty(myUsername)) return;
            
            // 1. 서버에서 받은 내 관계 적용
            bool hasMyRelations = false;
            if (relations != null && relations.Any())
            {
                foreach (var rel in relations)
                {
                    bool isMyRelation = rel.FactionA == myUsername || rel.FactionB == myUsername;
                    
                    if (isMyRelation)
                    {
                        hasMyRelations = true;
                        string otherFactionId = rel.FactionA == myUsername ? rel.FactionB : rel.FactionA;
                        ApplySingleRelation(otherFactionId, rel.Kind, rel.Goodwill);
                    }
                }
            }
            
            // 2. 내 관계가 없으면 (첫 접속) → 로컬 AI 관계를 서버에 업로드
            if (!hasMyRelations)
            {
                Log.Message("[HybridMP][FACTION] First connection - uploading AI faction relations");
                UploadLocalAIRelations(myUsername);
            }
            
            // 3. 다른 유저들과의 관계 중립 초기화
            InitializePlayerRelations(myUsername, relations);
            
            initialized = true;
            Log.Message($"[HybridMP][FACTION] Relations sync complete");
        }
        
        /// <summary>
        /// 로컬 AI 세력 관계를 서버에 업로드 (첫 접속 시)
        /// </summary>
        private static void UploadLocalAIRelations(string myUsername)
        {
            if (Faction.OfPlayer == null || Find.FactionManager == null) return;
            
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                // 플레이어 세력 제외, AI 세력만
                if (faction.IsPlayer || faction.def == null) continue;
                
                var relation = Faction.OfPlayer.RelationWith(faction, allowNull: true);
                if (relation == null) continue;
                
                // RimWorld 관계를 네트워크 관계로 변환
                FactionRelationKindNetwork kind = relation.kind switch
                {
                    FactionRelationKind.Hostile => FactionRelationKindNetwork.Hostile,
                    FactionRelationKind.Ally => FactionRelationKindNetwork.Ally,
                    _ => FactionRelationKindNetwork.Neutral
                };
                
                // 서버에 동기화 (DefName으로 저장)
                SyncRelationChange(myUsername, faction.def.defName, kind, relation.baseGoodwill, "Initial");
            }
            
            Log.Message("[HybridMP][FACTION] Uploaded local AI relations to server");
        }
        
        /// <summary>
        /// 다른 유저들과의 관계 중립 초기화
        /// TODO: PlayerList 구현 후 활성화
        /// 현재는 MP Attack/Enter 시점에 관계가 설정됨
        /// </summary>
        private static void InitializePlayerRelations(string myUsername, List<FactionRelationData> existingRelations)
        {
            // 현재 PlayerList가 NetworkManager에 없음
            // 유저 간 관계는 첫 상호작용(MP Attack/Enter) 시점에 설정
            // 향후 PlayerListPacket 수신 시 여기서 초기화하도록 개선 가능
            Log.Message("[HybridMP][FACTION] Player relations will be initialized on first interaction");
        }
        
        /// <summary>
        /// 단일 관계 로컬 적용
        /// </summary>
        private static void ApplySingleRelation(string otherFactionId, FactionRelationKindNetwork kind, int goodwill)
        {
            if (Faction.OfPlayer == null) return;
            
            // AI 세력 찾기 (DefName 또는 Name)
            var otherFaction = Find.FactionManager?.AllFactions
                .FirstOrDefault(f => f.def?.defName == otherFactionId || f.Name == otherFactionId);
            
            if (otherFaction == null)
            {
                Log.Warning($"[HybridMP][FACTION] Faction not found: {otherFactionId}");
                return;
            }
            
            // RimWorld 관계 종류로 변환
            FactionRelationKind rimKind = kind switch
            {
                FactionRelationKindNetwork.Hostile => FactionRelationKind.Hostile,
                FactionRelationKindNetwork.Ally => FactionRelationKind.Ally,
                _ => FactionRelationKind.Neutral
            };
            
            // 관계 설정
            try
            {
                Faction.OfPlayer.SetRelation(new FactionRelation(otherFaction, rimKind));
                
                // 우호도 직접 설정 (Reflection 필요할 수 있음)
                var relation = Faction.OfPlayer.RelationWith(otherFaction, allowNull: true);
                if (relation != null)
                {
                    // goodwill은 FactionRelation 내부에서 관리
                    // 바닐라에서는 Goodwill을 직접 수정, 여기서는 Kind만 적용
                }
                
                Log.Message($"[HybridMP][FACTION] Applied: {otherFaction.Name} = {rimKind}");
            }
            catch (Exception e)
            {
                Log.Error($"[HybridMP][FACTION] Failed to apply relation: {e.Message}");
            }
        }
        
        /// <summary>
        /// 관계 변경 시 서버에 동기화
        /// </summary>
        public static void SyncRelationChange(string factionA, string factionB, FactionRelationKindNetwork kind, int goodwill, string reason)
        {
            var packet = new FactionRelationSyncPacket
            {
                FactionA = factionA,
                FactionB = factionB,
                NewKind = kind,
                NewGoodwill = goodwill,
                Reason = reason
            };
            
            NetworkManager.Instance?.Send(packet);
            Log.Message($"[HybridMP][FACTION] Synced relation: {factionA} <-> {factionB} = {kind} ({reason})");
        }
        
        /// <summary>
        /// 적대 관계로 설정
        /// </summary>
        public static void SetHostile(string myFaction, string targetFaction, string reason = "Attack")
        {
            SyncRelationChange(myFaction, targetFaction, FactionRelationKindNetwork.Hostile, -100, reason);
        }
        
        /// <summary>
        /// 중립 관계로 설정
        /// </summary>
        public static void SetNeutral(string myFaction, string targetFaction, string reason = "Peace")
        {
            SyncRelationChange(myFaction, targetFaction, FactionRelationKindNetwork.Neutral, 0, reason);
        }
        
        /// <summary>
        /// 우호도 변경 동기화
        /// </summary>
        public static void SyncGoodwillChange(string factionA, string factionB, int newGoodwill, string reason)
        {
            FactionRelationKindNetwork kind;
            if (newGoodwill <= -75)
                kind = FactionRelationKindNetwork.Hostile;
            else if (newGoodwill >= 75)
                kind = FactionRelationKindNetwork.Ally;
            else
                kind = FactionRelationKindNetwork.Neutral;
            
            SyncRelationChange(factionA, factionB, kind, newGoodwill, reason);
        }
        
        /// <summary>
        /// 서버에서 받은 관계 변경 처리
        /// </summary>
        public static void OnRelationChanged(FactionRelationSyncPacket packet)
        {
            string myUsername = NetworkManager.Instance?.Username;
            
            // 내 관련 관계면 로컬 적용
            if (packet.FactionA == myUsername || packet.FactionB == myUsername)
            {
                string otherFactionId = packet.FactionA == myUsername ? packet.FactionB : packet.FactionA;
                ApplySingleRelation(otherFactionId, packet.NewKind, packet.NewGoodwill);
            }
            
            Log.Message($"[HybridMP][FACTION] Relation changed by server: {packet.FactionA} <-> {packet.FactionB} = {packet.NewKind}");
        }
    }
}
