using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using HybridShared.Packets;

namespace HybridServer
{
    /// <summary>
    /// 세력 관계 관리자
    /// - 양방향 관계 저장 (a-b == b-a)
    /// - 파일 저장/로드
    /// - 변경 시 브로드캐스트
    /// </summary>
    public class FactionRelationManager
    {
        private static FactionRelationManager _instance;
        public static FactionRelationManager Instance => _instance ??= new FactionRelationManager();
        
        private readonly string dataPath;
        private readonly Dictionary<string, FactionRelationData> relations = new();
        
        public FactionRelationManager()
        {
            dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "faction_relations.json");
            LoadRelations();
        }
        
        /// <summary>
        /// 관계 키 생성 (정렬하여 양방향 동일하게)
        /// </summary>
        private string MakeKey(string factionA, string factionB)
        {
            var sorted = new[] { factionA, factionB }.OrderBy(x => x).ToArray();
            return $"{sorted[0]}|{sorted[1]}";
        }
        
        /// <summary>
        /// 관계 조회 (없으면 기본값 중립 반환)
        /// </summary>
        public FactionRelationData GetRelation(string factionA, string factionB)
        {
            var key = MakeKey(factionA, factionB);
            
            if (relations.TryGetValue(key, out var relation))
                return relation;
            
            // 기본값: 중립
            return new FactionRelationData
            {
                FactionA = factionA,
                FactionB = factionB,
                Kind = FactionRelationKindNetwork.Neutral,
                Goodwill = 0
            };
        }
        
        /// <summary>
        /// 관계 설정 및 저장
        /// </summary>
        public void SetRelation(string factionA, string factionB, FactionRelationKindNetwork kind, int goodwill, string reason = null)
        {
            var key = MakeKey(factionA, factionB);
            var sorted = new[] { factionA, factionB }.OrderBy(x => x).ToArray();
            
            var relation = new FactionRelationData
            {
                FactionA = sorted[0],
                FactionB = sorted[1],
                Kind = kind,
                Goodwill = Math.Clamp(goodwill, -100, 100)
            };
            
            relations[key] = relation;
            SaveRelations();
            
            Console.WriteLine($"[FACTION] Relation changed: {factionA} <-> {factionB} = {kind} (Goodwill: {goodwill}) - {reason ?? "N/A"}");
        }
        
        /// <summary>
        /// 우호도 변경
        /// </summary>
        public void AdjustGoodwill(string factionA, string factionB, int delta, string reason = null)
        {
            var current = GetRelation(factionA, factionB);
            var newGoodwill = Math.Clamp(current.Goodwill + delta, -100, 100);
            
            // 우호도에 따른 관계 자동 변경
            FactionRelationKindNetwork newKind = current.Kind;
            if (newGoodwill <= -75)
                newKind = FactionRelationKindNetwork.Hostile;
            else if (newGoodwill >= 75)
                newKind = FactionRelationKindNetwork.Ally;
            else if (newGoodwill > -50 && newGoodwill < 50 && current.Kind != FactionRelationKindNetwork.Neutral)
                newKind = FactionRelationKindNetwork.Neutral;
            
            SetRelation(factionA, factionB, newKind, newGoodwill, reason);
        }
        
        /// <summary>
        /// 모든 관계 조회
        /// </summary>
        public List<FactionRelationData> GetAllRelations()
        {
            return relations.Values.ToList();
        }
        
        /// <summary>
        /// 특정 세력의 모든 관계 조회
        /// </summary>
        public List<FactionRelationData> GetRelationsFor(string factionId)
        {
            return relations.Values
                .Where(r => r.FactionA == factionId || r.FactionB == factionId)
                .ToList();
        }
        
        /// <summary>
        /// 파일에서 관계 로드
        /// </summary>
        public void LoadRelations()
        {
            try
            {
                if (File.Exists(dataPath))
                {
                    var json = File.ReadAllText(dataPath);
                    var data = JsonConvert.DeserializeObject<FactionRelationsFile>(json);
                    
                    relations.Clear();
                    foreach (var r in data?.Relations ?? new List<FactionRelationData>())
                    {
                        var key = MakeKey(r.FactionA, r.FactionB);
                        relations[key] = r;
                    }
                    
                    Console.WriteLine($"[FACTION] Loaded {relations.Count} relations from file");
                }
                else
                {
                    Console.WriteLine("[FACTION] No faction_relations.json found, starting fresh");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[FACTION] Error loading relations: {e.Message}");
            }
        }
        
        /// <summary>
        /// 파일에 관계 저장
        /// </summary>
        public void SaveRelations()
        {
            try
            {
                var dir = Path.GetDirectoryName(dataPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var data = new FactionRelationsFile
                {
                    Relations = relations.Values.ToList()
                };
                
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(dataPath, json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[FACTION] Error saving relations: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// 저장 파일 구조
    /// </summary>
    internal class FactionRelationsFile
    {
        public List<FactionRelationData> Relations { get; set; } = new();
    }
}
