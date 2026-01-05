namespace HybridShared
{
    /// <summary>
    /// 로그 카테고리.
    /// 문제 발생 위치 추적을 위한 체계적 분류.
    /// </summary>
    public enum LogCategory
    {
        /// <summary>전투 세션 시작/종료</summary>
        Battle,
        /// <summary>틱 동기화</summary>
        Tick,
        /// <summary>플레이어 액션</summary>
        Action,
        /// <summary>Desync 감지</summary>
        Desync,
        /// <summary>Fast Resync 적용</summary>
        Resync,
        /// <summary>네트워크 패킷</summary>
        Net,
        /// <summary>일반</summary>
        General,
        /// <summary>서버 관리</summary>
        Server,
        /// <summary>플레이어 관리</summary>
        Player,
        /// <summary>채팅</summary>
        Chat,
        /// <summary>로비</summary>
        Lobby
    }
    
    /// <summary>
    /// 하이브리드 멀티플레이어 로깅 시스템.
    /// 모든 로그에 [HybridMP][카테고리] 태그를 붙여 문제 추적을 용이하게 함.
    /// </summary>
    public static class HybridLogger
    {
        /// <summary>상세 로그 출력 여부 (성능 영향 있음)</summary>
        public static bool VerboseMode = true;
        
        /// <summary>로그 접두사</summary>
        private const string Prefix = "[HybridMP]";
        
        /// <summary>일반 로그</summary>
        public static void Log(LogCategory category, string message, string context = null)
        {
            string tag = $"{Prefix}[{category.ToString().ToUpper()}]";
            string ctx = !string.IsNullOrEmpty(context) ? $" ({context})" : "";
#if CLIENT
            Verse.Log.Message($"{tag} {message}{ctx}");
#else
            System.Console.WriteLine($"{tag} {message}{ctx}");
#endif
        }
        
        /// <summary>경고 로그</summary>
        public static void Warn(LogCategory category, string message, string context = null)
        {
            string tag = $"{Prefix}[{category.ToString().ToUpper()}]";
            string ctx = !string.IsNullOrEmpty(context) ? $" ({context})" : "";
#if CLIENT
            Verse.Log.Warning($"{tag} {message}{ctx}");
#else
            System.Console.WriteLine($"[WARN]{tag} {message}{ctx}");
#endif
        }
        
        /// <summary>에러 로그</summary>
        public static void Error(LogCategory category, string message, string context = null)
        {
            string tag = $"{Prefix}[{category.ToString().ToUpper()}]";
            string ctx = !string.IsNullOrEmpty(context) ? $" ({context})" : "";
#if CLIENT
            Verse.Log.Error($"{tag} {message}{ctx}");
#else
            System.Console.WriteLine($"[ERROR]{tag} {message}{ctx}");
#endif
        }
        
        /// <summary>
        /// 상세 로그 (디버그용).
        /// VerboseMode가 true일 때만 출력됨.
        /// </summary>
        public static void Verbose(LogCategory category, string message, string context = null)
        {
            if (!VerboseMode) return;
            Log(category, message, context);
        }
        
        /// <summary>일반 로그 (카테고리 없음)</summary>
        public static void Log(string message)
        {
            Log(LogCategory.General, message);
        }
        
        /// <summary>경고 로그 (카테고리 없음)</summary>
        public static void Warn(string message)
        {
            Warn(LogCategory.General, message);
        }
        
        /// <summary>에러 로그 (카테고리 없음)</summary>
        public static void Error(string message)
        {
            Error(LogCategory.General, message);
        }
    }
}
