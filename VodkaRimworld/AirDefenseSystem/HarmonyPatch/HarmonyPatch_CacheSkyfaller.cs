using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ADS;
using Verse;

using RimWorld;

namespace ADS.Patch
{
    [HarmonyPatch(typeof(SkyfallerMaker), "SpawnSkyfaller")]
    internal class HarmonyPatch_CacheSkyfaller1
    {
        public static void Postfix(ThingDef skyfaller, IntVec3 pos, Map map, Skyfaller __result)
        {
            __result.RegiserToMap(map);
        }
    }
    [HarmonyPatch(typeof(SkyfallerMaker), "SpawnSkyfaller")]
    internal class HarmonyPatch_CacheSkyfaller2
    {
        public static void Postfix(ThingDef skyfaller, ThingDef innerThing, IntVec3 pos, Map map, Skyfaller __result)
        {
            __result.RegiserToMap(map);
        }
    }
    [HarmonyPatch(typeof(SkyfallerMaker), "SpawnSkyfaller")]
    internal class HarmonyPatch_CacheSkyfaller3
    {
        public static void Postfix(ThingDef skyfaller, Thing innerThing, IntVec3 pos, Map map, Skyfaller __result)
        {
            __result.RegiserToMap(map);
        }
    }
    [HarmonyPatch(typeof(SkyfallerMaker), "SpawnSkyfaller")]
    internal class HarmonyPatch_CacheSkyfaller4
    {
        public static void Postfix(ThingDef skyfaller, IEnumerable<Thing> things, IntVec3 pos, Map map, Skyfaller __result)
        {
            __result.RegiserToMap(map);
        }
    }
}
