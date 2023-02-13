using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

using HarmonyLib;

namespace ADS.Patch
{
    [StaticConstructorOnStartup]
    internal static class HarmonyEntry
    {
        static HarmonyEntry()
        {
            Harmony harmony = new Harmony("Vodka.AirDefenseSystem");
            harmony.PatchAll();
        }
    }
}
