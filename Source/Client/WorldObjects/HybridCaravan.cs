using RimWorld.Planet;
using Verse;

namespace HybridClient.WorldObjects
{
    /// <summary>
    /// 다른 플레이어의 캐러밴을 월드 맵에 표시하는 커스텀 WorldObject
    /// RT의 RTCaravan 패턴 기반
    /// </summary>
    public class HybridCaravan : WorldObject
    {
        public string ownerUsername;
        public int caravanId;
        
        public string OwnerUsername
        {
            get => ownerUsername;
            set => ownerUsername = value;
        }
        
        public int CaravanId
        {
            get => caravanId;
            set => caravanId = value;
        }
        
        public override string Label => $"{ownerUsername}'s Caravan";
        
        public override string GetInspectString()
        {
            return $"Owner: {ownerUsername}\nCaravan ID: {caravanId}";
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ownerUsername, "ownerUsername");
            Scribe_Values.Look(ref caravanId, "caravanId");
        }
    }
}
