using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HybridShared;
using HybridShared.Packets;
using HybridClient.Patches;
using Newtonsoft.Json;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace HybridClient.World
{
    /// <summary>
    /// 클라이언트 월드 매니저 - 서버 월드 수신 및 RimWorld 월드 생성
    /// RT WorldManager.cs 기반 - HybridMP 적응
    /// </summary>
    public class ClientWorldManager
    {
        private static ClientWorldManager _instance;
        public static ClientWorldManager Instance => _instance ??= new ClientWorldManager();
        
        /// <summary>현재 서버 월드 설정</summary>
        public PlanetConfig CurrentWorld { get; private set; }
        
        /// <summary>월드가 로드되었는지</summary>
        public bool HasWorld => CurrentWorld != null;
        
        /// <summary>서버에서 월드 생성을 요청받았는지</summary>
        public bool IsCreatingNewWorld { get; private set; }
        
        // 이벤트
        public event Action OnWorldReceived;
        public event Action OnWorldGenerated;
        public event Action<PlayerSettlementInfo> OnSettlementCreated;
        
        private ClientWorldManager()
        {
            Log.Message("[HybridMP] ClientWorldManager initialized");
        }
        
        /// <summary>
        /// 서버로부터 월드 패킷 처리
        /// </summary>
        public void HandleWorldPacket(WorldPacket packet)
        {
            switch (packet.StepMode)
            {
                case WorldStepMode.RequestCreate:
                    // 서버가 새 월드 생성 요청 (첫 접속자)
                    OnWorldCreateRequested();
                    break;
                    
                case WorldStepMode.SendToClient:
                    // 서버가 기존 월드 전송
                    OnWorldReceived_Internal(packet.WorldData);
                    break;
            }
        }
        
        /// <summary>
        /// 서버가 월드 생성을 요청 (첫 접속자)
        /// RT: OnAskForWorld 패턴
        /// </summary>
        private void OnWorldCreateRequested()
        {
            Log.Message("[HybridMP][WORLD] Server requested world creation - you are the first player!");
            IsCreatingNewWorld = true;
            
            // 모든 기존 다이얼로그 닫기
            Find.WindowStack.TryRemove(typeof(Dialog_ConnectToServer), doCloseSound: false);
            
            // RT 방식: 직접 Page_SelectScenario 푸시
            // Find.WindowStack이 아닌 직접 페이지 전환
            Page_SelectScenario scenarioPage = new Page_SelectScenario();
            Find.WindowStack.Add(scenarioPage);
            
            Log.Message("[HybridMP][WORLD] Page_SelectScenario opened");
        }
        
        /// <summary>
        /// 서버로부터 월드 데이터 수신
        /// RT: OnReceiveWorld → OnExistingWorld 패턴
        /// </summary>
        private void OnWorldReceived_Internal(byte[] worldData)
        {
            try
            {
                var json = Encoding.UTF8.GetString(worldData);
                CurrentWorld = JsonConvert.DeserializeObject<PlanetConfig>(json);
                Log.Message($"[HybridMP][WORLD] World received from server: Seed={CurrentWorld.SeedString}");
                
                IsCreatingNewWorld = false;
                OnWorldReceived?.Invoke();
                
                // RT OnExistingWorld 패턴: 시나리오 선택으로 이동
                // 월드 생성은 게임 시작 시 자동으로 처리됨
                OnExistingWorld();
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] Failed to deserialize world data: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 기존 월드가 있는 경우 - RT OnExistingWorld 패턴
        /// 세이브가 있으면 로드, 없으면 새 정착지 선택
        /// </summary>
        private void OnExistingWorld()
        {
            Log.Message("[HybridMP][WORLD] Existing world received - checking for saved game...");
            
            // 플래그 리셋 (재접속 시 중요!)
            _saveLoadHandled = false;
            
            // 연결 다이얼로그 닫기
            Find.WindowStack.TryRemove(typeof(Dialog_ConnectToServer), doCloseSound: false);
            
            // 서버에 세이브 요청
            Save.ClientSaveManager.Instance.OnSaveReceived += OnSaveReceivedFromServer;
            Save.ClientSaveManager.Instance.RequestSaveFromServer();
            
            // 타임아웃 처리 (5초 후에도 응답 없으면 새 게임으로)
            System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Thread.Sleep(5000);
                
                // 아직 세이브를 받지 못했으면 새 게임 시작
                if (!_saveLoadHandled)
                {
                    Log.Message("[HybridMP][WORLD] Save request timeout - starting new game");
                    _saveLoadHandled = true;
                    LongEventHandler.ExecuteWhenFinished(() => StartNewSettlementSelection());
                }
            });
        }
        
        private bool _saveLoadHandled = false;
        
        /// <summary>
        /// 서버에서 세이브 수신 시 처리
        /// </summary>
        private void OnSaveReceivedFromServer(byte[] saveData)
        {
            // 이벤트 해제
            Save.ClientSaveManager.Instance.OnSaveReceived -= OnSaveReceivedFromServer;
            
            if (_saveLoadHandled) return;
            _saveLoadHandled = true;
            
            if (saveData != null && saveData.Length > 0)
            {
                // 세이브가 있으면 로드
                Log.Message("[HybridMP][WORLD] Save found - loading saved game...");
                
                // QueueLongEvent로 게임 로드 실행
                LongEventHandler.QueueLongEvent(() =>
                {
                    try
                    {
                        Save.ClientSaveManager.Instance.LoadPendingSave();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[HybridMP][WORLD] Failed to load save: {ex.Message}");
                        // 로드 실패 시 새 정착지 선택
                        StartNewSettlementSelection();
                    }
                }, "LoadingGame", doAsynchronously: false, null);
            }
            else
            {
                // 세이브가 없으면 새 정착지 선택
                Log.Message("[HybridMP][WORLD] No save found - starting new settlement selection");
                StartNewSettlementSelection();
            }
        }
        
        /// <summary>
        /// 새 정착지 선택 시작 (기존 월드에서)
        /// </summary>
        private void StartNewSettlementSelection()
        {
            _saveLoadHandled = false; // 리셋
            
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    // 게임 초기화
                    Current.Game = new Game();
                    Current.Game.InitData = new GameInitData();
                    
                    // 시나리오 설정
                    ApplyScenario();
                    
                    // 이야기꾼/난이도 설정
                    ApplyStoryteller();
                    
                    // 월드 생성
                    GenerateWorldFromConfig();
                }
                catch (Exception ex)
                {
                    Log.Error($"[HybridMP][WORLD] StartNewSettlementSelection failed: {ex.Message}");
                }
            }, "LoadingWorld", doAsynchronously: false, null);
        }
        
        /// <summary>
        /// 서버 설정에서 시나리오 적용
        /// </summary>
        private void ApplyScenario()
        {
            if (string.IsNullOrEmpty(CurrentWorld?.ScenarioDefName))
            {
                // 기본 시나리오
                Current.Game.Scenario = ScenarioLister.AllScenarios().FirstOrDefault() 
                    ?? ScenarioDefOf.Crashlanded.scenario;
                Log.Message("[HybridMP][WORLD] Using default scenario");
                return;
            }
            
            var scenario = ScenarioLister.AllScenarios()
                .FirstOrDefault(s => s.name == CurrentWorld.ScenarioDefName);
            
            if (scenario != null)
            {
                Current.Game.Scenario = scenario;
                Log.Message($"[HybridMP][WORLD] Applied scenario: {scenario.name}");
            }
            else
            {
                Current.Game.Scenario = ScenarioDefOf.Crashlanded.scenario;
                Log.Warning($"[HybridMP][WORLD] Scenario '{CurrentWorld.ScenarioDefName}' not found, using default");
            }
        }
        
        /// <summary>
        /// 서버 설정에서 이야기꾼/난이도 적용
        /// </summary>
        private void ApplyStoryteller()
        {
            // 이야기꾼
            StorytellerDef storytellerDef = null;
            if (!string.IsNullOrEmpty(CurrentWorld?.StorytellerDefName))
            {
                storytellerDef = DefDatabase<StorytellerDef>.GetNamedSilentFail(CurrentWorld.StorytellerDefName);
            }
            storytellerDef ??= StorytellerDefOf.Cassandra;
            
            // 난이도
            DifficultyDef difficultyDef = null;
            if (!string.IsNullOrEmpty(CurrentWorld?.DifficultyDefName))
            {
                difficultyDef = DefDatabase<DifficultyDef>.GetNamedSilentFail(CurrentWorld.DifficultyDefName);
            }
            difficultyDef ??= DifficultyDefOf.Rough;
            
            Current.Game.storyteller = new Storyteller(storytellerDef, difficultyDef);
            Log.Message($"[HybridMP][WORLD] Applied storyteller: {storytellerDef.defName}, difficulty: {difficultyDef.defName}");
        }
        
        /// <summary>
        /// 서버 설정으로 RimWorld 월드 생성
        /// </summary>
        public void GenerateWorldFromConfig()
        {
            if (CurrentWorld == null)
            {
                Log.Error("[HybridMP] No world config to generate");
                return;
            }
            
            Log.Message($"[HybridMP] Generating world with seed: {CurrentWorld.SeedString}");
            
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    // 게임 초기화
                    Find.GameInitData?.ResetWorldRelatedMapInitData();
                    
                    // 랜덤 시드 설정 (서버와 동일한 결과를 위해)
                    Rand.EnsureStateStackEmpty();
                    Rand.PushState(CurrentWorld.PersistentRandomValue);
                    
                    // 세력 Def 목록 생성
                    var factionDefs = GetFactionDefsFromConfig();
                    
                    // 월드 생성
                    Current.Game.World = WorldGenerator.GenerateWorld(
                        CurrentWorld.PlanetCoverage,
                        CurrentWorld.SeedString,
                        (OverallRainfall)CurrentWorld.Rainfall,
                        (OverallTemperature)CurrentWorld.Temperature,
                        (OverallPopulation)CurrentWorld.Population,
                        LandmarkDensity.Normal, // 기본값
                        factionDefs,
                        CurrentWorld.Pollution
                    );
                    
                    Rand.PopState();
                    
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        // 월드 렌더러 재생성
                        Find.World.renderer.RegenerateAllLayersNow();
                        Current.CreatingWorld = null;
                        
                        // 서버에서 받은 NPC 정착지가 있으면 적용 (기존 NPC 정착지 대체)
                        if (CurrentWorld?.NPCSettlements != null && CurrentWorld.NPCSettlements.Count > 0)
                        {
                            SetNPCSettlementsFromServer();
                        }
                        
                        // 서버에서 받은 도로가 있으면 적용
                        if (CurrentWorld?.Roads != null && CurrentWorld.Roads.Count > 0)
                        {
                            SetRoadsFromServer();
                        }
                        
                        // 서버에서 받은 야영지/사이트가 있으면 적용
                        if (CurrentWorld?.Sites != null && CurrentWorld.Sites.Count > 0)
                        {
                            SetSitesFromServer();
                        }
                        
                        // 플레이어 정착지 배치 (다른 플레이어들의 정착지)
                        SpawnPlayerSettlements();
                        
                        Log.Message("[HybridMP] World generation complete");
                        OnWorldGenerated?.Invoke();
                        
                        // 정착지 선택 페이지로 이동
                        ShowStartingSiteSelection();
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"[HybridMP] World generation failed: {ex.Message}");
                }
                
            }, "GeneratingWorld", doAsynchronously: true, null);
        }
        
        /// <summary>
        /// 세력 DefName에서 FactionDef 목록 생성
        /// </summary>
        private List<FactionDef> GetFactionDefsFromConfig()
        {
            var factionDefs = new List<FactionDef>();
            
            if (CurrentWorld.NPCFactions == null) return factionDefs;
            
            foreach (var factionInfo in CurrentWorld.NPCFactions)
            {
                var def = DefDatabase<FactionDef>.GetNamedSilentFail(factionInfo.DefName);
                if (def != null)
                {
                    factionDefs.Add(def);
                }
                else
                {
                    Log.Warning($"[HybridMP] Faction def not found: {factionInfo.DefName}");
                }
            }
            
            return factionDefs;
        }
        
        /// <summary>
        /// 서버에서 받은 플레이어 정착지들을 월드에 배치
        /// RT SettlementManager.SpawnSingleSettlement 패턴
        /// </summary>
        private void SpawnPlayerSettlements()
        {
            if (CurrentWorld?.PlayerSettlements == null || CurrentWorld.PlayerSettlements.Count == 0)
            {
                Log.Message("[HybridMP][SETTLEMENT] No player settlements to spawn");
                return;
            }
            
            Log.Message($"[HybridMP][SETTLEMENT] Spawning {CurrentWorld.PlayerSettlements.Count} player settlements...");
            
            foreach (var settlementInfo in CurrentWorld.PlayerSettlements)
            {
                try
                {
                    SpawnSinglePlayerSettlement(settlementInfo);
                }
                catch (Exception ex)
                {
                    Log.Error($"[HybridMP][SETTLEMENT] Failed to spawn settlement at tile {settlementInfo.TileId}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 단일 플레이어 정착지 생성
        /// </summary>
        private void SpawnSinglePlayerSettlement(PlayerSettlementInfo info)
        {
            // 이미 해당 타일에 정착지가 있는지 확인
            var existingSettlement = Find.WorldObjects.SettlementAt(info.TileId);
            if (existingSettlement != null)
            {
                Log.Message($"[HybridMP][SETTLEMENT] Tile {info.TileId} already has a settlement, skipping");
                return;
            }
            
            var myUsername = NetworkManager.Instance?.Username;
            bool isOwnSettlement = !string.IsNullOrEmpty(myUsername) && info.OwnerUsername == myUsername;
            
            // 세력 결정
            Faction faction;
            if (isOwnSettlement)
            {
                // 자기 정착지 - 플레이어 세력으로 생성
                faction = Faction.OfPlayer;
                Log.Message($"[HybridMP][SETTLEMENT] Spawning OWN settlement at tile {info.TileId}");
            }
            else
            {
                // 다른 플레이어의 정착지 - 해당 유저명으로 세력 생성/찾기
                faction = GetOrCreatePlayerFaction(info.OwnerUsername);
                
                if (faction == null)
                {
                    Log.Warning($"[HybridMP][SETTLEMENT] Failed to create faction for player '{info.OwnerUsername}'");
                    return;
                }
            }
            
            // Settlement WorldObject 생성
            var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.Tile = info.TileId;
            settlement.Name = info.SettlementName ?? $"{info.OwnerUsername}'s Colony";
            settlement.SetFaction(faction);
            
            Find.WorldObjects.Add(settlement);
            
            Log.Message($"[HybridMP][SETTLEMENT] Spawned '{settlement.Name}' at tile {info.TileId} (Owner: {info.OwnerUsername}, Faction: {faction.Name})");
        }
        
        /// <summary>
        /// 다른 플레이어용 세력 생성 또는 가져오기
        /// </summary>
        public Faction GetOrCreatePlayerFaction(string username)
        {
            // 이미 존재하는 플레이어 세력 찾기
            string factionName = $"{username}의 세력";
            var existingFaction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f.Name == factionName);
            
            if (existingFaction != null)
            {
                return existingFaction;
            }
            
            // 새 세력 생성 - Outlander Civil 기반
            try
            {
                var factionDef = FactionDefOf.OutlanderCivil;
                var newFaction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(factionDef));
                newFaction.Name = factionName;
                
                // 우호적으로 설정
                newFaction.TryAffectGoodwillWith(Faction.OfPlayer, 50, canSendMessage: false);
                
                Find.FactionManager.Add(newFaction);
                
                Log.Message($"[HybridMP][FACTION] Created faction for player: {factionName}");
                return newFaction;
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP][FACTION] Failed to create faction: {ex.Message}");
                
                // 폴백: 기존 중립 세력 사용
                return Find.FactionManager.AllFactions
                    .FirstOrDefault(f => !f.IsPlayer && !f.HostileTo(Faction.OfPlayer));
            }
        }
        
        /// <summary>
        /// 정착지 위치 선택 페이지 표시
        /// </summary>
        private void ShowStartingSiteSelection()
        {
            var selectSite = new Page_SelectStartingSite();
            var configurePawns = new Page_ConfigureStartingPawns();
            
            configurePawns.nextAct = PageUtility.InitGameStart;
            
            // 이념 DLC가 활성화된 경우
            if (ModsConfig.IdeologyActive)
            {
                var chooseIdeo = new Page_ChooseIdeoPreset();
                chooseIdeo.prev = selectSite;
                chooseIdeo.next = configurePawns;
                selectSite.next = chooseIdeo;
            }
            else
            {
                selectSite.next = configurePawns;
                configurePawns.prev = selectSite;
            }
            
            Find.WindowStack.Add(selectSite);
        }
        
        /// <summary>
        /// 로컬에서 생성한 월드 설정을 서버에 전송
        /// </summary>
        public void SendWorldToServer()
        {
            if (!IsCreatingNewWorld)
            {
                Log.Warning("[HybridMP] Not in world creation mode");
                return;
            }
            
            try
            {
                // 현재 게임 월드에서 설정 추출
                var world = Current.Game?.World;
                if (world == null)
                {
                    Log.Error("[HybridMP] No world to send");
                    return;
                }
                
                var config = new PlanetConfig
                {
                    SeedString = world.info.seedString,
                    PersistentRandomValue = world.info.persistentRandomValue,
                    PlanetCoverage = world.info.planetCoverage,
                    Rainfall = (int)world.info.overallRainfall,
                    Temperature = (int)world.info.overallTemperature,
                    Population = (int)world.info.overallPopulation,
                    Pollution = world.info.pollution,
                    // 게임 파라미터 저장
                    ScenarioDefName = Current.Game.Scenario?.name,
                    StorytellerDefName = Current.Game.storyteller?.def?.defName,
                    DifficultyDefName = Current.Game.storyteller?.difficultyDef?.defName,
                    NPCFactions = GetNPCFactionsFromWorld(),
                    NPCSettlements = GetNPCSettlementsFromWorld(),
                    Roads = GetRoadsFromWorld(),
                    Sites = GetSitesFromWorld(),
                    PlayerSettlements = new List<PlayerSettlementInfo>()
                };
                
                CurrentWorld = config;
                
                // 서버에 전송
                var packet = new WorldPacket
                {
                    StepMode = WorldStepMode.SendToServer,
                    WorldData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(config))
                };
                
                NetworkManager.Instance.Send(packet);
                
                Log.Message($"[HybridMP] World sent to server: Seed={config.SeedString}");
                IsCreatingNewWorld = false;
            }
            catch (Exception ex)
            {
                Log.Error($"[HybridMP] Failed to send world: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 월드에서 NPC 세력 정보 추출
        /// </summary>
        private List<NPCFactionInfo> GetNPCFactionsFromWorld()
        {
            var result = new List<NPCFactionInfo>();
            var world = Current.Game?.World;
            if (world == null) return result;
            
            foreach (var faction in world.factionManager.AllFactions)
            {
                if (faction == Faction.OfPlayer) continue;
                
                result.Add(new NPCFactionInfo
                {
                    DefName = faction.def.defName,
                    Name = faction.Name,
                    Color = new[] { faction.Color.r, faction.Color.g, faction.Color.b, faction.Color.a }
                });
            }
            
            return result;
        }
        
        /// <summary>
        /// 월드에서 NPC 정착지 정보 추출
        /// </summary>
        private List<NPCSettlementInfo> GetNPCSettlementsFromWorld()
        {
            var result = new List<NPCSettlementInfo>();
            var world = Current.Game?.World;
            if (world == null) return result;
            
            foreach (var settlement in world.worldObjects.Settlements)
            {
                if (settlement.Faction == Faction.OfPlayer) continue;
                
                result.Add(new NPCSettlementInfo
                {
                    TileId = settlement.Tile,
                    DefName = settlement.Faction?.def?.defName ?? "Unknown",
                    Name = settlement.Name,
                    FactionName = settlement.Faction?.Name ?? "Unknown"
                });
            }
            
            return result;
        }
        
        /// <summary>
        /// 서버에서 받은 NPC 정착지로 월드 정착지 설정 (RT 패턴)
        /// 기존 NPC 정착지를 제거하고 서버에서 받은 정착지로 대체
        /// </summary>
        private void SetNPCSettlementsFromServer()
        {
            if (CurrentWorld?.NPCSettlements == null || CurrentWorld.NPCSettlements.Count == 0)
            {
                Log.Message("[HybridMP][NPC] No NPC settlements from server");
                return;
            }
            
            Log.Message($"[HybridMP][NPC] Setting {CurrentWorld.NPCSettlements.Count} NPC settlements from server");
            
            // 기존 NPC 정착지 제거 (플레이어 정착지는 제외)
            var settlementsToRemove = new List<Settlement>();
            foreach (var settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction != null && !settlement.Faction.IsPlayer)
                {
                    settlementsToRemove.Add(settlement);
                }
            }
            
            foreach (var settlement in settlementsToRemove)
            {
                Find.WorldObjects.Remove(settlement);
            }
            Log.Message($"[HybridMP][NPC] Removed {settlementsToRemove.Count} existing NPC settlements");
            
            // 서버에서 받은 NPC 정착지 스폰
            int spawned = 0;
            foreach (var info in CurrentWorld.NPCSettlements)
            {
                try
                {
                    // 해당 세력 찾기
                    Faction faction = Find.FactionManager.AllFactions
                        .FirstOrDefault(f => f.def.defName == info.DefName);
                    
                    if (faction == null)
                    {
                        Log.Warning($"[HybridMP][NPC] Faction not found: {info.DefName}");
                        continue;
                    }
                    
                    // 정착지 생성
                    var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                    settlement.Tile = info.TileId;
                    settlement.Name = info.Name;
                    settlement.SetFaction(faction);
                    
                    Find.WorldObjects.Add(settlement);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HybridMP][NPC] Failed to spawn NPC settlement at {info.TileId}: {ex.Message}");
                }
            }
            
            Log.Message($"[HybridMP][NPC] Spawned {spawned} NPC settlements from server");
        }
        
        /// <summary>
        /// 월드에서 도로 정보 추출 (RT GetPlanetRoads 패턴)
        /// </summary>
        private List<RoadDetail> GetRoadsFromWorld()
        {
            var result = new List<RoadDetail>();
            var world = Current.Game?.World;
            if (world?.grid == null) return result;
            
            var addedPairs = new HashSet<(int, int)>();
            
            // RT 패턴: WorldGrid.tiles는 Tile[] 타입
            for (int i = 0; i < world.grid.TilesCount; i++)
            {
                var tile = world.grid[i];
                if (tile.Roads != null)
                {
                    foreach (var link in tile.Roads)
                    {
                        // 중복 방지 (A->B와 B->A는 같은 도로)
                        var pair = (Math.Min(i, link.neighbor), Math.Max(i, link.neighbor));
                        if (addedPairs.Contains(pair)) continue;
                        addedPairs.Add(pair);
                        
                        result.Add(new RoadDetail
                        {
                            FromTile = i,
                            ToTile = link.neighbor,
                            DefName = link.road?.defName ?? "DirtRoad"
                        });
                    }
                }
            }
            
            Log.Message($"[HybridMP][ROAD] Collected {result.Count} roads from world");
            return result;
        }
        
        /// <summary>
        /// 월드에서 야영지/사이트 정보 추출
        /// </summary>
        private List<SiteInfo> GetSitesFromWorld()
        {
            var result = new List<SiteInfo>();
            var world = Current.Game?.World;
            if (world == null) return result;
            
            foreach (var wo in world.worldObjects.AllWorldObjects)
            {
                // Settlement이 아닌 Site 형태의 WorldObject 수집
                if (wo is Site site)
                {
                    result.Add(new SiteInfo
                    {
                        TileId = site.Tile,
                        DefName = site.def?.defName ?? "Unknown",
                        FactionDefName = site.Faction?.def?.defName
                    });
                }
            }
            
            Log.Message($"[HybridMP][SITE] Collected {result.Count} sites from world");
            return result;
        }
        
        /// <summary>
        /// 서버에서 받은 도로 적용 (RT AddRoads 패턴)
        /// </summary>
        private void SetRoadsFromServer()
        {
            if (CurrentWorld?.Roads == null || CurrentWorld.Roads.Count == 0)
            {
                Log.Message("[HybridMP][ROAD] No roads from server");
                return;
            }
            
            Log.Message($"[HybridMP][ROAD] Setting {CurrentWorld.Roads.Count} roads from server");
            
            int added = 0;
            foreach (var road in CurrentWorld.Roads)
            {
                try
                {
                    var roadDef = DefDatabase<RoadDef>.GetNamedSilentFail(road.DefName);
                    if (roadDef == null)
                    {
                        Log.Warning($"[HybridMP][ROAD] Road def not found: {road.DefName}");
                        continue;
                    }
                    
                    // RT 패턴: WorldGrid[tileId]로 Tile 접근
                    AddRoadLink(road.FromTile, road.ToTile, roadDef);
                    AddRoadLink(road.ToTile, road.FromTile, roadDef);
                    added++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HybridMP][ROAD] Failed to add road {road.FromTile}->{road.ToTile}: {ex.Message}");
                }
            }
            
            // 월드 렌더러 갱신
            Find.World.renderer.RegenerateAllLayersNow();
            Log.Message($"[HybridMP][ROAD] Added {added} roads from server");
        }
        
        /// <summary>
        /// 타일에 도로 링크 추가 (RT AddRoadLink 패턴)
        /// RimWorld 1.6: SurfaceTile 캐스팅으로 Roads/potentialRoads 접근
        /// </summary>
        private void AddRoadLink(int tileId, int neighborTileId, RoadDef roadDef)
        {
            try
            {
                // RT 패턴: Find.WorldGrid[tileId]를 SurfaceTile로 캐스팅
                SurfaceTile tile = (SurfaceTile)Find.WorldGrid[tileId];
                
                // 이미 존재하는 도로 확인
                if (tile.Roads != null)
                {
                    foreach (var link in tile.Roads)
                    {
                        if (link.neighbor == neighborTileId) return; // 이미 존재
                    }
                }
                
                // 새 도로 링크 추가
                tile.potentialRoads ??= new List<SurfaceTile.RoadLink>();
                tile.potentialRoads.Add(new SurfaceTile.RoadLink
                {
                    neighbor = neighborTileId,
                    road = roadDef
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"[HybridMP][ROAD] AddRoadLink failed: {tileId} -> {neighborTileId}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 서버에서 받은 야영지/사이트 적용
        /// </summary>
        private void SetSitesFromServer()
        {
            if (CurrentWorld?.Sites == null || CurrentWorld.Sites.Count == 0)
            {
                Log.Message("[HybridMP][SITE] No sites from server");
                return;
            }
            
            Log.Message($"[HybridMP][SITE] Setting {CurrentWorld.Sites.Count} sites from server");
            
            // 기존 사이트 제거
            var sitesToRemove = new List<Site>();
            foreach (var wo in Find.WorldObjects.AllWorldObjects)
            {
                if (wo is Site site && site.Faction != Faction.OfPlayer)
                {
                    sitesToRemove.Add(site);
                }
            }
            foreach (var site in sitesToRemove)
            {
                Find.WorldObjects.Remove(site);
            }
            
            // 서버에서 받은 사이트 스폰
            int spawned = 0;
            foreach (var info in CurrentWorld.Sites)
            {
                try
                {
                    var siteDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail(info.DefName);
                    if (siteDef == null) continue;
                    
                    Faction faction = null;
                    if (!string.IsNullOrEmpty(info.FactionDefName))
                    {
                        faction = Find.FactionManager.AllFactions
                            .FirstOrDefault(f => f.def.defName == info.FactionDefName);
                    }
                    
                    var site = (Site)WorldObjectMaker.MakeWorldObject(siteDef);
                    site.Tile = info.TileId;
                    if (faction != null) site.SetFaction(faction);
                    
                    Find.WorldObjects.Add(site);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[HybridMP][SITE] Failed to spawn site at {info.TileId}: {ex.Message}");
                }
            }
            
            Log.Message($"[HybridMP][SITE] Spawned {spawned} sites from server");
        }
        
        /// <summary>
        /// 정착지 생성 요청
        /// </summary>
        public void RequestSettlementCreate(int tileId, string settlementName)
        {
            var packet = new SettlementCreatePacket
            {
                TileId = tileId,
                SettlementName = settlementName
            };
            
            NetworkManager.Instance.Send(packet);
            Log.Message($"[HybridMP] Settlement creation requested: {settlementName} at tile {tileId}");
        }
        
        /// <summary>
        /// 정착지 생성 응답 처리
        /// </summary>
        public void HandleSettlementResponse(SettlementCreateResponsePacket packet)
        {
            if (packet.Success)
            {
                Log.Message($"[HybridMP] Settlement created: {packet.Settlement?.SettlementName}");
                OnSettlementCreated?.Invoke(packet.Settlement);
                
                // TODO: 맵에 정착지 표시
            }
            else
            {
                Log.Warning($"[HybridMP] Settlement creation failed: {packet.Message}");
                Messages.Message($"Settlement creation failed: {packet.Message}", MessageTypeDefOf.RejectInput);
            }
        }
        
        /// <summary>
        /// 정착지 목록 업데이트 - 새 정착지를 월드에 추가
        /// </summary>
        public void HandleSettlementList(SettlementListPacket packet)
        {
            if (CurrentWorld == null || Find.World == null)
            {
                return;
            }
            
            var newSettlements = packet.Settlements ?? new System.Collections.Generic.List<PlayerSettlementInfo>();
            var myUsername = NetworkManager.Instance?.Username;
            
            Log.Message($"[HybridMP] Settlement list received: {newSettlements.Count} settlements");
            
            // 기존 목록과 비교하여 새로 추가된 정착지 찾기
            var existingTiles = new System.Collections.Generic.HashSet<int>();
            foreach (var s in CurrentWorld.PlayerSettlements)
            {
                existingTiles.Add(s.TileId);
            }
            
            // 새로 추가된 정착지를 월드에 생성
            foreach (var info in newSettlements)
            {
                // 내 정착지는 건너뜀 (이미 내가 생성함)
                if (info.OwnerUsername == myUsername)
                    continue;
                
                // 이미 존재하는 정착지는 건너뜀
                if (existingTiles.Contains(info.TileId))
                    continue;
                
                // 해당 타일에 이미 정착지가 있는지 확인
                var existingSettlement = Find.WorldObjects.SettlementAt(info.TileId);
                if (existingSettlement != null)
                    continue;
                
                // 새 정착지 생성
                try
                {
                    var faction = GetOrCreatePlayerFaction(info.OwnerUsername);
                    var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                    settlement.Tile = info.TileId;
                    settlement.SetFaction(faction);
                    settlement.Name = info.SettlementName ?? $"{info.OwnerUsername}'s Colony";
                    
                    Find.WorldObjects.Add(settlement);
                    
                    Log.Message($"[HybridMP] Added new settlement: {settlement.Name} at tile {info.TileId} (owner: {info.OwnerUsername})");
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[HybridMP] Failed to add settlement at tile {info.TileId}: {ex.Message}");
                }
            }
            
            // 삭제된 정착지 처리 - 기존 목록에는 있지만 새 목록에는 없는 정착지 제거
            var serverTiles = new System.Collections.Generic.HashSet<int>();
            foreach (var s in newSettlements)
            {
                serverTiles.Add(s.TileId);
            }
            
            // 기존 목록의 타 플레이어 정착지 중 새 목록에 없는 것 찾기
            var tilesToRemove = new System.Collections.Generic.List<int>();
            foreach (var s in CurrentWorld.PlayerSettlements)
            {
                // 내 정착지는 건너뜀
                if (s.OwnerUsername == myUsername)
                    continue;
                
                // 새 목록에 없으면 삭제 대상
                if (!serverTiles.Contains(s.TileId))
                {
                    tilesToRemove.Add(s.TileId);
                }
            }
            
            // 월드에서 해당 타일의 정착지 제거
            foreach (var tile in tilesToRemove)
            {
                var settlement = Find.WorldObjects.SettlementAt(tile);
                if (settlement != null && settlement.Faction != Faction.OfPlayer)
                {
                    Log.Message($"[HybridMP] Removing abandoned settlement at tile {tile}");
                    Find.WorldObjects.Remove(settlement);
                }
            }
            
            // 메모리 업데이트
            CurrentWorld.PlayerSettlements = newSettlements;
        }
        
        /// <summary>
        /// 세이브 로드 후 서버 정착지 동기화 - RT BuildPlanet 패턴
        /// 1. 기존 타 플레이어 정착지 모두 삭제
        /// 2. 서버에서 받은 목록으로 새로 생성
        /// </summary>
        public void SyncSettlementsAfterLoad()
        {
            if (CurrentWorld == null || Find.World == null)
            {
                Log.Warning("[HybridMP] Cannot sync settlements - world not ready");
                return;
            }
            
            var serverSettlements = CurrentWorld.PlayerSettlements;
            var myUsername = NetworkManager.Instance?.Username;
            
            Log.Message($"[HybridMP] Syncing {serverSettlements?.Count ?? 0} settlements after load...");
            
            // 1단계: 기존 타 플레이어 정착지 모두 삭제 (RT ClearAllSettlements 패턴)
            var settlementsToRemove = new System.Collections.Generic.List<Settlement>();
            foreach (var settlement in Find.WorldObjects.Settlements)
            {
                // 내 정착지는 유지
                if (settlement.Faction == Faction.OfPlayer)
                    continue;
                
                // 타 플레이어 정착지인지 확인 (세력 이름에 "의 세력" 포함)
                if (settlement.Faction?.Name?.Contains("의 세력") == true ||
                    settlement.Faction?.Name?.Contains("'s Faction") == true)
                {
                    settlementsToRemove.Add(settlement);
                }
            }
            
            foreach (var settlement in settlementsToRemove)
            {
                Log.Message($"[HybridMP] Clearing old player settlement at tile {settlement.Tile}");
                Find.WorldObjects.Remove(settlement);
            }
            
            // 2단계: 서버 목록으로 새로 생성 (RT AddSettlements 패턴)
            if (serverSettlements == null || serverSettlements.Count == 0)
            {
                Log.Message("[HybridMP] No server settlements to sync");
                return;
            }
            
            foreach (var info in serverSettlements)
            {
                // 내 정착지는 건너뜀 (내 세이브에 이미 있음)
                if (info.OwnerUsername == myUsername)
                    continue;
                
                // 해당 타일에 이미 정착지가 있는지 확인 (NPC 등)
                var existingSettlement = Find.WorldObjects.SettlementAt(info.TileId);
                if (existingSettlement != null)
                {
                    continue;
                }
                
                // 새 정착지 생성
                try
                {
                    var faction = GetOrCreatePlayerFaction(info.OwnerUsername);
                    var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                    settlement.Tile = info.TileId;
                    settlement.SetFaction(faction);
                    settlement.Name = info.SettlementName ?? $"{info.OwnerUsername}'s Colony";
                    
                    Find.WorldObjects.Add(settlement);
                    
                    Log.Message($"[HybridMP] Added server settlement: {settlement.Name} at tile {info.TileId}");
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[HybridMP] Failed to add settlement at tile {info.TileId}: {ex.Message}");
                }
            }
            
            Log.Message("[HybridMP] Settlement sync after load complete");
        }
        
        /// <summary>
        /// 세이브 로드 후 서버 캐러밴 동기화
        /// 다른 플레이어의 캐러밴을 월드에 표시
        /// </summary>
        public void SyncCaravansAfterLoad()
        {
            if (Find.World == null)
            {
                Log.Warning("[HybridMP] Cannot sync caravans - world not ready");
                return;
            }
            
            // GuestCaravans (서버에서 받은 타 플레이어 캐러밴 목록) 사용
            var guestCaravans = CaravanSync.ClientCaravanManager.Instance.GuestCaravans;
            var myUsername = NetworkManager.Instance?.Username;
            
            Log.Message($"[HybridMP] Syncing {guestCaravans?.Count ?? 0} guest caravans after load...");
            
            if (guestCaravans == null || guestCaravans.Count == 0)
            {
                return;
            }
            
            // 다른 플레이어의 캐러밴을 월드에 표시 (ToList로 복사본 순회)
            foreach (var info in guestCaravans.ToList())
            {
                // 내 캐러밴은 건너뜀 (내 세이브에 이미 있음)
                if (info.OwnerUsername == myUsername)
                    continue;
                
                // 이미 월드에 있는지 확인
                bool exists = false;
                foreach (var wo in Find.WorldObjects.AllWorldObjects)
                {
                    if (wo is WorldObjects.HybridCaravan hc && hc.CaravanId == info.CaravanId)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;
                
                // HybridCaravan WorldObject 생성
                try
                {
                    var faction = GetOrCreatePlayerFaction(info.OwnerUsername);
                    var caravan = (WorldObjects.HybridCaravan)WorldObjectMaker.MakeWorldObject(Defs.HybridWorldObjectDefOf.HybridCaravan);
                    caravan.Tile = info.Tile;
                    caravan.SetFaction(faction);
                    caravan.OwnerUsername = info.OwnerUsername;
                    caravan.CaravanId = info.CaravanId;
                    
                    Find.WorldObjects.Add(caravan);
                    CaravanSync.ClientCaravanManager.Instance.GuestCaravans.Add(info);
                    
                    Log.Message($"[HybridMP] Added guest caravan: {info.OwnerUsername} at tile {info.Tile}");
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[HybridMP] Failed to create HybridCaravan: {ex.Message}");
                }
            }
            
            Log.Message("[HybridMP] Caravan sync after load complete");
        }
    }
}



