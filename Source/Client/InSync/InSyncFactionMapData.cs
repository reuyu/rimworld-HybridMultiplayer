using System.Collections.Generic;
using Verse;
using RimWorld;

namespace HybridClient.InSync
{
    /// <summary>
    /// 세력별 맵 데이터 관리
    /// MP FactionMapData 패턴 적용
    /// 각 세력이 독립적으로 Designation, Area, Zone 관리
    /// </summary>
    public class InSyncFactionMapData
    {
        public int FactionId { get; private set; }
        public Map Map { get; private set; }
        
        // 저장되는 매니저들
        public DesignationManager DesignationManager { get; private set; }
        public AreaManager AreaManager { get; private set; }
        public ZoneManager ZoneManager { get; private set; }
        
        // 저장되지 않는 매니저들 (게임 시작 시 재생성)
        public HaulDestinationManager HaulDestinationManager { get; private set; }
        public ListerHaulables ListerHaulables { get; private set; }
        public ResourceCounter ResourceCounter { get; private set; }
        
        private InSyncFactionMapData() { }
        
        /// <summary>
        /// 새 세력용 FactionMapData 생성
        /// </summary>
        public static InSyncFactionMapData CreateNew(int factionId, Map map)
        {
            var data = new InSyncFactionMapData
            {
                FactionId = factionId,
                Map = map,
                DesignationManager = new DesignationManager(map),
                AreaManager = new AreaManager(map),
                ZoneManager = new ZoneManager(map),
                HaulDestinationManager = new HaulDestinationManager(map),
                ListerHaulables = new ListerHaulables(map),
                ResourceCounter = new ResourceCounter(map)
            };
            
            // 기본 영역 추가
            data.AreaManager.AddStartingAreas();
            
            Log.Message($"[HybridMP][FACTIONMAP] Created new FactionMapData for faction {factionId}");
            return data;
        }
        
        /// <summary>
        /// 기존 맵에서 FactionMapData 추출
        /// </summary>
        public static InSyncFactionMapData CreateFromMap(int factionId, Map map)
        {
            var data = new InSyncFactionMapData
            {
                FactionId = factionId,
                Map = map,
                DesignationManager = map.designationManager,
                AreaManager = map.areaManager,
                ZoneManager = map.zoneManager,
                HaulDestinationManager = map.haulDestinationManager,
                ListerHaulables = map.listerHaulables,
                ResourceCounter = map.resourceCounter
            };
            
            Log.Message($"[HybridMP][FACTIONMAP] Created FactionMapData from map for faction {factionId}");
            return data;
        }
        
        /// <summary>
        /// 이 세력의 매니저들을 맵에 적용
        /// </summary>
        public void ApplyToMap()
        {
            // 맵의 매니저들을 이 세력의 것으로 교체
            // Reflection 사용 필요 (맵 필드가 readonly)
            try
            {
                var mapType = typeof(Map);
                
                var designationField = HarmonyLib.AccessTools.Field(mapType, "designationManager");
                designationField?.SetValue(Map, DesignationManager);
                
                var areaField = HarmonyLib.AccessTools.Field(mapType, "areaManager");
                areaField?.SetValue(Map, AreaManager);
                
                var zoneField = HarmonyLib.AccessTools.Field(mapType, "zoneManager");
                zoneField?.SetValue(Map, ZoneManager);
                
                var haulField = HarmonyLib.AccessTools.Field(mapType, "haulDestinationManager");
                haulField?.SetValue(Map, HaulDestinationManager);
                
                var listerField = HarmonyLib.AccessTools.Field(mapType, "listerHaulables");
                listerField?.SetValue(Map, ListerHaulables);
                
                var resourceField = HarmonyLib.AccessTools.Field(mapType, "resourceCounter");
                resourceField?.SetValue(Map, ResourceCounter);
                
                Log.Message($"[HybridMP][FACTIONMAP] Applied faction {FactionId} data to map");
            }
            catch (System.Exception e)
            {
                Log.Error($"[HybridMP][FACTIONMAP] Failed to apply FactionMapData: {e}");
            }
        }
    }
    
    /// <summary>
    /// 맵별 세력 데이터 관리자
    /// </summary>
    public static class InSyncFactionMapManager
    {
        // 맵 ID -> (세력 ID -> FactionMapData)
        private static Dictionary<int, Dictionary<int, InSyncFactionMapData>> mapFactionData 
            = new Dictionary<int, Dictionary<int, InSyncFactionMapData>>();
        
        /// <summary>
        /// 맵의 세력 데이터 초기화 (InSync 시작 시)
        /// </summary>
        public static void InitializeForMap(Map map)
        {
            if (map == null) return;
            
            int mapId = map.uniqueID;
            
            if (!mapFactionData.ContainsKey(mapId))
            {
                mapFactionData[mapId] = new Dictionary<int, InSyncFactionMapData>();
            }
            
            // 현재 플레이어 세력 데이터 저장
            var playerFaction = Faction.OfPlayer;
            if (playerFaction != null)
            {
                mapFactionData[mapId][playerFaction.loadID] = 
                    InSyncFactionMapData.CreateFromMap(playerFaction.loadID, map);
            }
            
            Log.Message($"[HybridMP][FACTIONMAP] Initialized for map {mapId}");
        }
        
        /// <summary>
        /// 새 세력의 맵 데이터 생성
        /// </summary>
        public static void CreateForFaction(Map map, Faction faction)
        {
            if (map == null || faction == null) return;
            
            int mapId = map.uniqueID;
            
            if (!mapFactionData.ContainsKey(mapId))
            {
                mapFactionData[mapId] = new Dictionary<int, InSyncFactionMapData>();
            }
            
            if (!mapFactionData[mapId].ContainsKey(faction.loadID))
            {
                mapFactionData[mapId][faction.loadID] = 
                    InSyncFactionMapData.CreateNew(faction.loadID, map);
            }
        }
        
        /// <summary>
        /// 세력 컨텍스트 스왑 시 맵 데이터도 교체
        /// </summary>
        public static void SwapToFaction(Map map, Faction newFaction)
        {
            if (map == null || newFaction == null) return;
            
            int mapId = map.uniqueID;
            
            if (mapFactionData.TryGetValue(mapId, out var factionDict))
            {
                if (factionDict.TryGetValue(newFaction.loadID, out var data))
                {
                    data.ApplyToMap();
                }
            }
        }
        
        /// <summary>
        /// InSync 종료 시 정리
        /// </summary>
        public static void Cleanup(Map map)
        {
            if (map == null) return;
            
            mapFactionData.Remove(map.uniqueID);
            Log.Message($"[HybridMP][FACTIONMAP] Cleaned up map {map.uniqueID}");
        }
        
        /// <summary>
        /// 전체 정리
        /// </summary>
        public static void CleanupAll()
        {
            mapFactionData.Clear();
            Log.Message("[HybridMP][FACTIONMAP] Cleaned up all");
        }
    }
}
