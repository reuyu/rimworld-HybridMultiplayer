using System;
using Verse;

namespace HybridClient.InSync
{
    /// <summary>
    /// InSync 중 랜덤 시드 제어
    /// MP Seeds 패턴 적용 - 결정론적 실행 보장
    /// RT+MP 하이브리드: InSync 중에만 적용
    /// </summary>
    public static class InSyncRandHelper
    {
        /// <summary>InSync 중 사용되는 공유 랜덤 상태</summary>
        public static ulong SharedRandState { get; set; } = 1;
        
        /// <summary>
        /// InSync 맵 진입 시 시드 초기화
        /// 권위자와 침입자 양쪽에서 동일한 시드로 시작
        /// </summary>
        public static void InitializeForInSync(int mapId, int sessionId)
        {
            // 맵 ID와 세션 ID를 조합하여 결정론적 시드 생성
            int seed = Gen.HashCombineInt(mapId, sessionId);
            SharedRandState = (ulong)seed;
            
            Log.Message($"[HybridMP][RAND] InSync rand initialized. Map: {mapId}, Session: {sessionId}, Seed: {seed}");
        }
        
        /// <summary>
        /// InSync 작업 전 시드 Push
        /// 특정 작업에서 결정론적 결과 보장
        /// </summary>
        public static void PushState(int contextSeed)
        {
            if (!InSyncManager.Instance.IsActive)
                return;
            
            Rand.PushState(contextSeed);
        }
        
        /// <summary>
        /// InSync 작업 후 시드 Pop
        /// </summary>
        public static void PopState()
        {
            if (!InSyncManager.Instance.IsActive)
                return;
            
            Rand.PopState();
        }
        
        /// <summary>
        /// 맵 컨텍스트에서 시드 Push (맵 고유 ID 사용)
        /// MP SeedMapLoad 패턴
        /// </summary>
        public static void PushMapContext(Map map)
        {
            if (!InSyncManager.Instance.IsActive || map == null)
                return;
            
            Rand.PushState(map.uniqueID);
        }
        
        /// <summary>
        /// Pawn 컨텍스트에서 시드 Push (thingIDNumber 사용)
        /// MP SeedPawnGraphics 패턴
        /// </summary>
        public static void PushPawnContext(Pawn pawn)
        {
            if (!InSyncManager.Instance.IsActive || pawn == null)
                return;
            
            Rand.PushState(pawn.thingIDNumber);
        }
        
        /// <summary>
        /// Thing 스폰 시 시드 Push
        /// MP GenSpawnRotatePatch 패턴
        /// </summary>
        public static void PushSpawnContext(Thing thing)
        {
            if (!InSyncManager.Instance.IsActive || thing == null)
                return;
            
            Rand.PushState(thing.thingIDNumber);
        }
        
        /// <summary>
        /// 현재 랜덤 상태 저장 (Desync 감지용)
        /// RimWorld의 Rand는 StateCompressed가 없으므로 대체 방식 사용
        /// </summary>
        public static void CaptureRandState()
        {
            if (!InSyncManager.Instance.IsActive)
                return;
            
            // Rand.Int를 호출하면 상태가 변경되므로, 현재 상태 해시를 기록
            // 실제 상태를 읽을 수는 없지만, 같은 시점에 같은 값이면 동기화된 것으로 간주
            SharedRandState = (ulong)Gen.HashCombineInt(Rand.Int, Rand.Int);
        }
        
        /// <summary>
        /// InSync 종료 시 정리
        /// </summary>
        public static void Cleanup()
        {
            SharedRandState = 1;
            Log.Message("[HybridMP][RAND] InSync rand state cleaned up");
        }
    }
}
