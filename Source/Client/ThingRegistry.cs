using System;
using System.Collections.Generic;
using Verse;

namespace HybridClient
{
    /// <summary>
    /// ThingID 기반 객체 레지스트리
    /// 서버-클라이언트 간 객체 매핑 관리
    /// </summary>
    public class ThingRegistry
    {
        private static ThingRegistry _instance;
        public static ThingRegistry Instance => _instance ??= new ThingRegistry();
        
        // ThingID -> Thing 매핑
        private readonly Dictionary<int, Thing> thingsById = new();
        
        // 역방향 매핑 (Thing -> ThingID)
        private readonly Dictionary<Thing, int> idsByThing = new();
        
        public int Count => thingsById.Count;
        
        /// <summary>
        /// ThingID로 Thing 조회
        /// </summary>
        public Thing Get(int thingId)
        {
            return thingsById.TryGetValue(thingId, out var thing) ? thing : null;
        }
        
        /// <summary>
        /// Thing에서 ThingID 조회
        /// </summary>
        public int GetId(Thing thing)
        {
            return idsByThing.TryGetValue(thing, out var id) ? id : -1;
        }
        
        /// <summary>
        /// Thing 등록
        /// </summary>
        public void Register(Thing thing)
        {
            if (thing == null) return;
            
            int id = thing.thingIDNumber;
            thingsById[id] = thing;
            idsByThing[thing] = id;
        }
        
        /// <summary>
        /// Thing 제거
        /// </summary>
        public void Unregister(int thingId)
        {
            if (thingsById.TryGetValue(thingId, out var thing))
            {
                thingsById.Remove(thingId);
                idsByThing.Remove(thing);
            }
        }
        
        /// <summary>
        /// Thing 제거
        /// </summary>
        public void Unregister(Thing thing)
        {
            if (thing == null) return;
            Unregister(thing.thingIDNumber);
        }
        
        /// <summary>
        /// 맵의 모든 Thing 등록
        /// </summary>
        public void RegisterMap(Map map)
        {
            if (map == null) return;
            
            foreach (var thing in map.listerThings.AllThings)
            {
                Register(thing);
            }
            
            Log.Message($"[HybridMP] ThingRegistry: Registered {thingsById.Count} things from map {map.uniqueID}");
        }
        
        /// <summary>
        /// ThingID로 Thing 조회, 없으면 null
        /// </summary>
        public T Get<T>(int thingId) where T : Thing
        {
            return Get(thingId) as T;
        }
        
        /// <summary>
        /// ThingID로 Pawn 조회
        /// </summary>
        public Pawn GetPawn(int thingId)
        {
            return Get<Pawn>(thingId);
        }
        
        /// <summary>
        /// 레지스트리 초기화
        /// </summary>
        public void Clear()
        {
            thingsById.Clear();
            idsByThing.Clear();
            Log.Message("[HybridMP] ThingRegistry cleared");
        }
        
        /// <summary>
        /// 맵에서 ThingID로 Thing 찾기 (레지스트리에 없으면 맵에서 검색)
        /// </summary>
        public Thing FindInMap(Map map, int thingId)
        {
            // 먼저 레지스트리에서 찾기
            var cached = Get(thingId);
            if (cached != null && !cached.Destroyed)
                return cached;
            
            // 맵에서 검색
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.thingIDNumber == thingId)
                {
                    Register(thing);
                    return thing;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// 폰 존재 여부 확인 (서버 상태와 비교용)
        /// </summary>
        public bool Exists(int thingId)
        {
            var thing = Get(thingId);
            return thing != null && !thing.Destroyed;
        }
    }
}
