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
    [HarmonyPatch(typeof(Skyfaller), "SpawnSetup")]
    internal class HarmonyPatch_CacheSkyfaller1
    {
        public static void Postfix(Map map, bool respawningAfterLoad, Skyfaller __instance)
        {
            if (map == null) return;
            __instance.RegiserToMap(map);
        }
    }
}
