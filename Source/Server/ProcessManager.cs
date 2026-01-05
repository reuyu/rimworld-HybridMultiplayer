using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using HybridShared;

namespace HybridServer
{
    /// <summary>
    /// InSync Server (헤드리스 RimWorld 인스턴스) 프로세스 관리자.
    /// 실시간 동기화가 필요한 세션을 위한 별도 프로세스 생성/관리.
    /// </summary>
    public class ProcessManager
    {
        private static ProcessManager _instance;
        public static ProcessManager Instance => _instance ??= new ProcessManager();
        
        // 사용 가능한 포트 풀 (35000-35999 - 메인 서버와 분리)
        private ConcurrentQueue<int> availablePorts = new();
        
        // 활성화된 InSync Server 프로세스 (포트 -> 프로세스 정보)
        private ConcurrentDictionary<int, InSyncServerInfo> activeServers = new();
        
        // 포트 범위 설정
        private const int PORT_RANGE_START = 35000;
        private const int PORT_RANGE_END = 35999;
        
        // RimWorld 실행 파일 경로 (설정 필요)
        public string RimWorldExecutablePath { get; set; }
        
        public int ActiveServerCount => activeServers.Count;
        public int AvailablePortCount => availablePorts.Count;
        
        private ProcessManager()
        {
            InitializePortPool();
            HybridLogger.Log(LogCategory.Server, 
                "ProcessManager initialized", 
                $"Port range: {PORT_RANGE_START}-{PORT_RANGE_END}");
        }
        
        /// <summary>
        /// 포트 풀 초기화
        /// </summary>
        private void InitializePortPool()
        {
            int available = 0;
            for (int i = PORT_RANGE_START; i <= PORT_RANGE_END; i++)
            {
                if (IsPortAvailable(i))
                {
                    availablePorts.Enqueue(i);
                    available++;
                }
            }
            
            HybridLogger.Log(LogCategory.Server, 
                $"Port pool initialized: {available} ports available");
        }
        
        /// <summary>
        /// 포트가 사용 가능한지 확인
        /// </summary>
        private bool IsPortAvailable(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// InSync Server 시작 (현재는 시뮬레이션 - 실제 프로세스 생성 없음)
        /// </summary>
        /// <param name="sessionId">InSync 세션 ID</param>
        /// <param name="saveFile">로드할 세이브 파일 경로 (optional)</param>
        /// <returns>InSyncServerInfo (포트 정보 포함)</returns>
        public InSyncServerInfo StartInSyncServer(string sessionId, string saveFile = null)
        {
            // 사용 가능한 포트 할당
            if (!availablePorts.TryDequeue(out int port))
            {
                HybridLogger.Error(LogCategory.Server, 
                    "No available ports for InSync Server");
                return null;
            }
            
            var serverInfo = new InSyncServerInfo
            {
                SessionId = sessionId,
                Port = port,
                SaveFile = saveFile,
                StartTime = DateTime.UtcNow,
                IsSimulated = true // 현재는 실제 프로세스 없음
            };
            
            // 실제 프로세스 시작은 나중에 구현
            // 지금은 같은 서버에서 InSync 세션을 처리
            
            activeServers[port] = serverInfo;
            
            HybridLogger.Log(LogCategory.Server, 
                $"InSync Server allocated",
                $"SessionId: {sessionId}, Port: {port}");
            
            return serverInfo;
        }
        
        /// <summary>
        /// 실제 RimWorld 프로세스 시작 (고급 기능 - 나중에 사용)
        /// </summary>
        public InSyncServerInfo StartRimWorldProcess(string sessionId, string saveFile)
        {
            if (string.IsNullOrEmpty(RimWorldExecutablePath) || !File.Exists(RimWorldExecutablePath))
            {
                HybridLogger.Error(LogCategory.Server, 
                    "RimWorld executable not configured",
                    $"Path: {RimWorldExecutablePath ?? "(not set)"}");
                return null;
            }
            
            if (!availablePorts.TryDequeue(out int port))
            {
                HybridLogger.Error(LogCategory.Server, "No available ports");
                return null;
            }
            
            try
            {
                string saveArg = string.IsNullOrEmpty(saveFile) ? "" : $"-savefile=\"{saveFile}\"";
                
                var psi = new ProcessStartInfo
                {
                    FileName = RimWorldExecutablePath,
                    Arguments = $"-batchmode -nographics {saveArg} -servermode -port={port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                HybridLogger.Log(LogCategory.Server, 
                    $"Starting RimWorld process",
                    $"Port: {port}, Args: {psi.Arguments}");
                
                var process = Process.Start(psi);
                
                if (process == null)
                {
                    HybridLogger.Error(LogCategory.Server, "Failed to start process");
                    availablePorts.Enqueue(port);
                    return null;
                }
                
                var serverInfo = new InSyncServerInfo
                {
                    SessionId = sessionId,
                    Port = port,
                    SaveFile = saveFile,
                    StartTime = DateTime.UtcNow,
                    Process = process,
                    IsSimulated = false
                };
                
                // 출력 캡처
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        HybridLogger.Verbose(LogCategory.Server, $"[InSync:{port}] {e.Data}");
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        HybridLogger.Error(LogCategory.Server, $"[InSync:{port}] {e.Data}");
                };
                
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // 프로세스 종료 이벤트
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) =>
                {
                    HybridLogger.Log(LogCategory.Server, $"InSync Server exited: {sessionId}");
                    StopInSyncServer(port);
                };
                
                activeServers[port] = serverInfo;
                
                HybridLogger.Log(LogCategory.Server, 
                    $"RimWorld process started",
                    $"PID: {process.Id}, Port: {port}");
                
                return serverInfo;
            }
            catch (Exception ex)
            {
                HybridLogger.Error(LogCategory.Server, 
                    $"Error starting process: {ex.Message}");
                availablePorts.Enqueue(port);
                return null;
            }
        }
        
        /// <summary>
        /// InSync Server 중지
        /// </summary>
        public void StopInSyncServer(int port)
        {
            if (activeServers.TryRemove(port, out var serverInfo))
            {
                try
                {
                    if (serverInfo.Process != null && !serverInfo.Process.HasExited)
                    {
                        serverInfo.Process.Kill();
                        serverInfo.Process.WaitForExit(5000);
                        serverInfo.Process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    HybridLogger.Warn(LogCategory.Server, 
                        $"Error stopping InSync Server: {ex.Message}");
                }
                finally
                {
                    availablePorts.Enqueue(port);
                    HybridLogger.Log(LogCategory.Server, 
                        $"Port {port} returned to pool");
                }
            }
        }
        
        /// <summary>
        /// 세션 ID로 서버 찾기
        /// </summary>
        public InSyncServerInfo GetBySessionId(string sessionId)
        {
            foreach (var info in activeServers.Values)
            {
                if (info.SessionId == sessionId)
                    return info;
            }
            return null;
        }
        
        /// <summary>
        /// 모든 InSync Server 중지
        /// </summary>
        public void StopAll()
        {
            foreach (var port in activeServers.Keys.ToArray())
            {
                StopInSyncServer(port);
            }
            HybridLogger.Log(LogCategory.Server, "All InSync Servers stopped");
        }
        
        /// <summary>
        /// 활성 서버 목록
        /// </summary>
        public List<InSyncServerInfo> GetActiveServers()
        {
            return new List<InSyncServerInfo>(activeServers.Values);
        }
        
        /// <summary>
        /// 콘솔 상태 출력
        /// </summary>
        public void PrintStatus()
        {
            Console.WriteLine($"[ProcessManager] Active servers: {activeServers.Count}");
            Console.WriteLine($"[ProcessManager] Available ports: {availablePorts.Count}");
            foreach (var info in activeServers.Values)
            {
                Console.WriteLine($"  - Session: {info.SessionId}, Port: {info.Port}, Uptime: {info.Uptime:hh\\:mm\\:ss}");
            }
        }
    }
    
    /// <summary>
    /// InSync Server 정보
    /// </summary>
    public class InSyncServerInfo
    {
        public string SessionId { get; set; }
        public int Port { get; set; }
        public string SaveFile { get; set; }
        public DateTime StartTime { get; set; }
        public Process Process { get; set; }
        public bool IsSimulated { get; set; }
        
        public TimeSpan Uptime => DateTime.UtcNow - StartTime;
        public bool IsRunning => IsSimulated || (Process != null && !Process.HasExited);
    }
}
