using System;
using System.Collections.Generic;
using System.Linq;
using HybridShared.Packets;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace HybridClient.CaravanSync
{
    /// <summary>
    /// 클라이언트 캐러밴 매니저 - RT CaravanManager 패턴 기반
    /// 캐러밴 생성/삭제/이동 시 서버에 동기화
    /// </summary>
    public class ClientCaravanManager
    {
        private static ClientCaravanManager _instance;
        public static ClientCaravanManager Instance => _instance ??= new ClientCaravanManager();
        
        // 추적 중인 플레이어 캐러밴
        public List<RimWorld.Planet.Caravan> PlayerCaravans { get; } = new List<RimWorld.Planet.Caravan>();
        
        // 다른 플레이어 캐러밴 정보
        public List<CaravanInfo> GuestCaravans { get; } = new List<CaravanInfo>();
        
        private ClientCaravanManager()
        {
            Log.Message("[HybridMP][CARAVAN] ClientCaravanManager initialized");
        }
        
        /// <summary>
        /// 캐러밴 생성 시 서버에 알림
        /// </summary>
        public void OnCaravanCreated(RimWorld.Planet.Caravan caravan)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            if (caravan.Faction != Faction.OfPlayer)
                return;
            
            PlayerCaravans.Add(caravan);
            
            var packet = new CaravanUpdatePacket
            {
                StepMode = CaravanStepMode.Add,
                Caravan = new CaravanInfo
                {
                    Tile = caravan.Tile,
                    OwnerUsername = NetworkManager.Instance.Username,
                    CaravanId = caravan.ID
                }
            };
            
            NetworkManager.Instance.Send(packet);
            Log.Message($"[HybridMP][CARAVAN] Caravan created at tile {caravan.Tile}, notified server");
        }
        
        /// <summary>
        /// 캐러밴 삭제 시 서버에 알림
        /// </summary>
        public void OnCaravanRemoved(RimWorld.Planet.Caravan caravan)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            if (!PlayerCaravans.Contains(caravan))
                return;
            
            PlayerCaravans.Remove(caravan);
            
            var packet = new CaravanUpdatePacket
            {
                StepMode = CaravanStepMode.Remove,
                Caravan = new CaravanInfo
                {
                    Tile = caravan.Tile,
                    OwnerUsername = NetworkManager.Instance.Username,
                    CaravanId = caravan.ID
                }
            };
            
            NetworkManager.Instance.Send(packet);
            Log.Message($"[HybridMP][CARAVAN] Caravan removed at tile {caravan.Tile}, notified server");
        }
        
        /// <summary>
        /// 캐러밴 이동 시 서버에 알림
        /// </summary>
        public void OnCaravanMoved(RimWorld.Planet.Caravan caravan, int newTile)
        {
            if (NetworkManager.Instance?.IsConnected != true)
                return;
            
            if (caravan.Faction != Faction.OfPlayer)
                return;
            
            var packet = new CaravanUpdatePacket
            {
                StepMode = CaravanStepMode.Move,
                Caravan = new CaravanInfo
                {
                    Tile = newTile,
                    OwnerUsername = NetworkManager.Instance.Username,
                    CaravanId = caravan.ID
                }
            };
            
            NetworkManager.Instance.Send(packet);
            // 너무 자주 로그 출력 방지
        }
        
        /// <summary>
        /// 서버에서 캐러밴 목록 수신
        /// </summary>
        public void HandleCaravanList(CaravanListPacket packet)
        {
            GuestCaravans.Clear();
            
            foreach (var info in packet.Caravans)
            {
                // 자기 캐러밴 제외
                if (info.OwnerUsername == NetworkManager.Instance?.Username)
                    continue;
                
                GuestCaravans.Add(info);
            }
            
            // TODO: 월드에 다른 플레이어 캐러밴 표시
            Log.Message($"[HybridMP][CARAVAN] Received {GuestCaravans.Count} guest caravans");
        }
        
        /// <summary>
        /// 서버에서 캐러밴 업데이트 수신
        /// </summary>
        public void HandleCaravanUpdate(CaravanUpdatePacket packet)
        {
            // 자기 캐러밴이면 무시
            if (packet.Caravan.OwnerUsername == NetworkManager.Instance?.Username)
                return;
            
            switch (packet.StepMode)
            {
                case CaravanStepMode.Add:
                    // 중복 체크 - 이미 같은 ID 캐러밴이 있으면 무시
                    bool alreadyExists = GuestCaravans.Any(c => 
                        c.CaravanId == packet.Caravan.CaravanId && 
                        c.OwnerUsername == packet.Caravan.OwnerUsername);
                    
                    if (!alreadyExists)
                    {
                        GuestCaravans.Add(packet.Caravan);
                        // 월드에 HybridCaravan 추가
                        AddHybridCaravanToWorld(packet.Caravan);
                        Log.Message($"[HybridMP][CARAVAN] Guest caravan added: {packet.Caravan.OwnerUsername} at tile {packet.Caravan.Tile}");
                    }
                    else
                    {
                        Log.Warning($"[HybridMP][CARAVAN] Duplicate caravan ignored: {packet.Caravan.OwnerUsername} ID {packet.Caravan.CaravanId}");
                    }
                    break;
                    
                case CaravanStepMode.Remove:
                    GuestCaravans.RemoveAll(c => c.CaravanId == packet.Caravan.CaravanId && c.OwnerUsername == packet.Caravan.OwnerUsername);
                    // 월드에서 HybridCaravan 삭제
                    RemoveHybridCaravanFromWorld(packet.Caravan.CaravanId);
                    Log.Message($"[HybridMP][CARAVAN] Guest caravan removed: {packet.Caravan.OwnerUsername}");
                    break;
                    
                case CaravanStepMode.Move:
                    var existing = GuestCaravans.FirstOrDefault(c => c.CaravanId == packet.Caravan.CaravanId && c.OwnerUsername == packet.Caravan.OwnerUsername);
                    if (existing != null)
                    {
                        existing.Tile = packet.Caravan.Tile;
                    }
                    // 월드에서 HybridCaravan 위치 업데이트
                    UpdateHybridCaravanPosition(packet.Caravan.CaravanId, packet.Caravan.Tile);
                    break;
            }
        }
        
        /// <summary>
        /// 월드에 HybridCaravan 추가
        /// </summary>
        private void AddHybridCaravanToWorld(CaravanInfo info)
        {
            if (Find.World == null) return;
            
            try
            {
                var faction = World.ClientWorldManager.Instance?.GetOrCreatePlayerFaction(info.OwnerUsername);
                var caravan = (WorldObjects.HybridCaravan)WorldObjectMaker.MakeWorldObject(Defs.HybridWorldObjectDefOf.HybridCaravan);
                caravan.Tile = info.Tile;
                caravan.SetFaction(faction);
                caravan.OwnerUsername = info.OwnerUsername;
                caravan.CaravanId = info.CaravanId;
                
                Find.WorldObjects.Add(caravan);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[HybridMP][CARAVAN] Failed to add HybridCaravan: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 월드에서 HybridCaravan 삭제
        /// </summary>
        private void RemoveHybridCaravanFromWorld(int caravanId)
        {
            if (Find.World == null) return;
            
            var toRemove = Find.WorldObjects.AllWorldObjects
                .OfType<WorldObjects.HybridCaravan>()
                .FirstOrDefault(hc => hc.CaravanId == caravanId);
            
            if (toRemove != null)
            {
                Find.WorldObjects.Remove(toRemove);
            }
        }
        
        /// <summary>
        /// HybridCaravan 위치 업데이트
        /// </summary>
        private void UpdateHybridCaravanPosition(int caravanId, int newTile)
        {
            if (Find.World == null) return;
            
            var caravan = Find.WorldObjects.AllWorldObjects
                .OfType<WorldObjects.HybridCaravan>()
                .FirstOrDefault(hc => hc.CaravanId == caravanId);
            
            if (caravan != null)
            {
                caravan.Tile = newTile;
            }
        }
        
        /// <summary>
        /// 모든 플레이어 캐러밴 추적 시작 및 서버에 등록
        /// 게임 로드 후 호출되어 세이브에 있던 캐러밴들을 서버에 등록
        /// </summary>
        public void TrackAllPlayerCaravans()
        {
            PlayerCaravans.Clear();
            
            foreach (var caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.Faction == Faction.OfPlayer)
                {
                    PlayerCaravans.Add(caravan);
                    
                    // 서버에 캐러밴 등록 (게임 로드 시 기존 캐러밴)
                    if (NetworkManager.Instance?.IsConnected == true)
                    {
                        var packet = new CaravanUpdatePacket
                        {
                            StepMode = CaravanStepMode.Add,
                            Caravan = new CaravanInfo
                            {
                                Tile = caravan.Tile,
                                OwnerUsername = NetworkManager.Instance.Username,
                                CaravanId = caravan.ID
                            }
                        };
                        NetworkManager.Instance.Send(packet);
                        Log.Message($"[HybridMP][CARAVAN] Registered existing caravan at tile {caravan.Tile} to server");
                    }
                }
            }
            
            Log.Message($"[HybridMP][CARAVAN] Tracking {PlayerCaravans.Count} player caravans");
        }
    }
}
