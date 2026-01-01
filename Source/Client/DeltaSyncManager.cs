using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using HybridShared.Packets;

namespace HybridClient
{
    /// <summary>
    /// 델타 동기화 매니저 - Thing 상태 추적 및 동기화
    /// </summary>
    public class DeltaSyncManager
    {
        private static DeltaSyncManager _instance;
        public static DeltaSyncManager Instance => _instance ??= new DeltaSyncManager();
        
        // 마지막으로 서버에 보낸 상태
        private Dictionary<int, ThingSnapshot> lastSentState = new();
        
        // 서버로부터 받은 권위 있는 상태
        private Dictionary<int, ThingSnapshot> authoritativeState = new();
        
        // 변경 감지 플래그
        private HashSet<int> dirtyThings = new();
        
        public bool HasDirtyThings => dirtyThings.Count > 0;
        
        /// <summary>
        /// 현재 맵의 모든 Thing 스냅샷 생성
        /// </summary>
        public List<ThingSnapshot> CaptureMapState(Map map)
        {
            var snapshots = new List<ThingSnapshot>();
            
            if (map == null) return snapshots;
            
            // 모든 Thing 순회
            foreach (var thing in map.listerThings.AllThings)
            {
                var snapshot = CreateSnapshot(thing);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }
            
            return snapshots;
        }
        
        /// <summary>
        /// Thing에서 스냅샷 생성
        /// </summary>
        public ThingSnapshot CreateSnapshot(Thing thing)
        {
            if (thing == null || thing.Destroyed) return null;
            
            var snapshot = new ThingSnapshot
            {
                ThingID = thing.thingIDNumber,
                DefName = thing.def.defName,
                X = thing.Position.x,
                Y = thing.Position.y,
                Z = thing.Position.z,
                HitPointsPercent = (float)thing.HitPoints / thing.MaxHitPoints,
                FactionId = thing.Faction?.loadID ?? -1
            };
            
            // Pawn 추가 정보
            if (thing is Pawn pawn)
            {
                snapshot.IsPawn = true;
                snapshot.CurrentJobDef = pawn.CurJob?.def?.defName;
                snapshot.IsDrafted = pawn.Drafted;
                snapshot.IsDowned = pawn.Downed;
                snapshot.IsDead = pawn.Dead;
            }
            
            return snapshot;
        }
        
        /// <summary>
        /// 이전 상태와 비교하여 변경된 Thing 찾기
        /// </summary>
        public List<ThingDeltaData> DetectChanges(List<ThingSnapshot> currentState)
        {
            var deltas = new List<ThingDeltaData>();
            var currentDict = currentState.ToDictionary(s => s.ThingID);
            
            // 1. 새로 생성되거나 변경된 Thing
            foreach (var snapshot in currentState)
            {
                if (!lastSentState.TryGetValue(snapshot.ThingID, out var lastSnapshot))
                {
                    // 새로 생성됨
                    deltas.Add(new ThingDeltaData
                    {
                        ThingID = snapshot.ThingID,
                        Type = DeltaType.Created,
                        Snapshot = snapshot
                    });
                }
                else if (!snapshot.Equals(lastSnapshot))
                {
                    // 변경됨 - 변경 타입 판단
                    var deltaType = DetectDeltaType(lastSnapshot, snapshot);
                    deltas.Add(new ThingDeltaData
                    {
                        ThingID = snapshot.ThingID,
                        Type = deltaType,
                        Snapshot = snapshot
                    });
                }
            }
            
            // 2. 파괴된 Thing
            foreach (var lastId in lastSentState.Keys)
            {
                if (!currentDict.ContainsKey(lastId))
                {
                    deltas.Add(new ThingDeltaData
                    {
                        ThingID = lastId,
                        Type = DeltaType.Destroyed,
                        Snapshot = null
                    });
                }
            }
            
            return deltas;
        }
        
        /// <summary>
        /// 변경 타입 판단
        /// </summary>
        private DeltaType DetectDeltaType(ThingSnapshot old, ThingSnapshot current)
        {
            // 위치 변경
            if (Math.Abs(old.X - current.X) > 0.1f || Math.Abs(old.Z - current.Z) > 0.1f)
                return DeltaType.Moved;
            
            // 체력 변경
            if (Math.Abs(old.HitPointsPercent - current.HitPointsPercent) > 0.01f)
                return DeltaType.Damaged;
            
            // 상태 변경 (Pawn)
            if (old.IsDrafted != current.IsDrafted || old.CurrentJobDef != current.CurrentJobDef)
                return DeltaType.StateChanged;
            
            return DeltaType.StateChanged;
        }
        
        /// <summary>
        /// 현재 상태를 "마지막으로 보낸 상태"로 저장
        /// </summary>
        public void UpdateLastSentState(List<ThingSnapshot> state)
        {
            lastSentState = state.ToDictionary(s => s.ThingID);
        }
        
        /// <summary>
        /// 서버로부터 받은 권위 있는 상태 적용
        /// </summary>
        public void ApplyAuthoritativeState(AuthoritativeStatePacket packet, Map map)
        {
            if (map == null) return;
            
            Log.Message($"[HybridMP] Applying {packet.Corrections.Count} corrections, " +
                       $"{packet.OrphanedThingIDs.Count} orphans, {packet.MissingThings.Count} missing");
            
            // 1. 수정 적용
            foreach (var delta in packet.Corrections)
            {
                ApplyCorrection(delta, map);
            }
            
            // 2. 클라이언트에만 있는 Thing 삭제 (주의: 실제 삭제는 위험할 수 있음)
            foreach (var thingId in packet.OrphanedThingIDs)
            {
                var thing = ThingRegistry.Instance.Get(thingId);
                if (thing != null && !thing.Destroyed)
                {
                    Log.Warning($"[HybridMP] Orphaned thing {thingId} should be removed");
                    // thing.Destroy(); // 실제 삭제는 신중하게
                }
            }
            
            // 3. 누락된 Thing 생성 (복잡 - 나중에 구현)
            foreach (var snapshot in packet.MissingThings)
            {
                Log.Warning($"[HybridMP] Missing thing {snapshot.ThingID} ({snapshot.DefName}) should be spawned");
                // SpawnThing(snapshot, map);
            }
        }
        
        /// <summary>
        /// 개별 수정 적용
        /// </summary>
        private void ApplyCorrection(ThingDeltaData delta, Map map)
        {
            var thing = ThingRegistry.Instance.Get(delta.ThingID);
            if (thing == null || delta.Snapshot == null) return;
            
            var snapshot = delta.Snapshot;
            
            switch (delta.Type)
            {
                case DeltaType.Moved:
                    // 위치 강제 이동
                    var newPos = new IntVec3((int)snapshot.X, (int)snapshot.Y, (int)snapshot.Z);
                    if (thing.Position != newPos && newPos.InBounds(map))
                    {
                        thing.Position = newPos;
                        Log.Message($"[HybridMP] Moved {delta.ThingID} to {newPos}");
                    }
                    break;
                    
                case DeltaType.Damaged:
                    // 체력 강제 설정
                    int targetHp = (int)(snapshot.HitPointsPercent * thing.MaxHitPoints);
                    if (thing.HitPoints != targetHp)
                    {
                        thing.HitPoints = targetHp;
                        Log.Message($"[HybridMP] Set HP of {delta.ThingID} to {targetHp}");
                    }
                    break;
                    
                case DeltaType.StateChanged:
                    if (thing is Pawn pawn)
                    {
                        // 징집 상태 강제 설정
                        if (pawn.drafter != null && pawn.Drafted != snapshot.IsDrafted)
                        {
                            pawn.drafter.Drafted = snapshot.IsDrafted;
                            Log.Message($"[HybridMP] Set Draft of {delta.ThingID} to {snapshot.IsDrafted}");
                        }
                    }
                    break;
            }
        }
        
        /// <summary>
        /// 상태 해시 계산 (빠른 비교용)
        /// </summary>
        public int ComputeStateHash(List<ThingSnapshot> snapshots)
        {
            int hash = 0;
            foreach (var snapshot in snapshots.OrderBy(s => s.ThingID))
            {
                hash ^= snapshot.GetHashCode();
            }
            return hash;
        }
        
        /// <summary>
        /// 클라이언트 상태 패킷 생성
        /// </summary>
        public ClientStatePacket CreateClientStatePacket(Map map, int clientTick)
        {
            var snapshots = CaptureMapState(map);
            return new ClientStatePacket
            {
                ClientTick = clientTick,
                Things = snapshots,
                StateHash = ComputeStateHash(snapshots)
            };
        }
        
        /// <summary>
        /// 초기화
        /// </summary>
        public void Clear()
        {
            lastSentState.Clear();
            authoritativeState.Clear();
            dirtyThings.Clear();
        }
    }
}
