using RimWorld;
using RimWorld.Planet;
using Verse;

namespace HybridClient.Defs
{
    /// <summary>
    /// HybridMP 커스텀 WorldObjectDef 참조
    /// </summary>
    [DefOf]
    public static class HybridWorldObjectDefOf
    {
        public static WorldObjectDef HybridCaravan;
        
        static HybridWorldObjectDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HybridWorldObjectDefOf));
        }
    }
}
