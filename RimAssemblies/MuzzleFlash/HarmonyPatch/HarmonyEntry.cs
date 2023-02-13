using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using HarmonyLib;
using Verse;

namespace MuzzleFlash.Patch
{
    [StaticConstructorOnStartup]
    internal static class HarmonyEntry
    {
        static HarmonyEntry()
        {
            Harmony harmony = new Harmony("Vodka.MuzzleFlash");
            harmony.PatchAll();
        }
    }
}
