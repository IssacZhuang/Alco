using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MTA.Patch
{
    internal class HarmonyEntry
    {
        static HarmonyEntry()
        {
            Harmony harmony = new Harmony("Vodka.MechTakeAmmoCE");
            harmony.PatchAll();
        }
    }
}
