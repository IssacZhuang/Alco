using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using HarmonyLib;
using UnityEngine;

namespace MuzzleFlash.HarmonyPatch
{
    [HarmonyPatch(typeof(PawnRenderer), "DrawEquipmentAiming")]
    internal class HarmonyPatch_PawnRenderer
    {
        public static void Postfix(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            WeaponPositionCache.SetDrawPos(eq, drawLoc);
        }
    }
}
