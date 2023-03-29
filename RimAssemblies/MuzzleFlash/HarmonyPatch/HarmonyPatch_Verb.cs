using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using HarmonyLib;
using Verse;
using UnityEngine;

namespace MuzzleFlash.Patch
{
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    internal class HarmonyPatch_Verb
    {
        public static void Postfix(Verb __instance, int ___burstShotsLeft)
        {
            Thing caster = __instance.Caster;
            if (caster == null) return;
            if (!(__instance.DirectOwner is ThingComp)) return;

            Thing equipment = __instance.EquipmentSource;

            if (equipment == null) return;

            if (!MuzzleFlashUtility.MuzzleFlashAvailable(__instance)) return;

            MuzzleFlashProps primary = null;
            MuzzleFlashProps secondary = null;
            equipment?.def?.GetMuzzleFlashProps(out primary, out secondary);

            MuzzleFlashProps muzzleProps = __instance.IsPrimaryVerb() ? primary : secondary;
            if (muzzleProps == null || muzzleProps.offsets == null || muzzleProps.offsets.NullOrEmpty()) return;

            if (muzzleProps.type == WeaponMode.NoPatch) return;

            Vector3 targetPosition = __instance.CurrentTarget.CenterVector3;
            Vector3 sourcePostion = caster.DrawPos;
            Vector3 direction = (targetPosition - sourcePostion);
            Vector3 drawPos;

            if (__instance.CasterIsPawn && !muzzleProps.useCenterAsRootPosition)
            {
                drawPos = WeaponPositionCache.GetDrawPos(equipment);
            }
            else
            {
                drawPos = sourcePostion;
            }
            direction.y = 0;
            direction.Normalize();

            if (muzzleProps.isAlternately)
            {
                int index = __instance.verbProps.burstShotCount == 1 ? Rand.Range(0, muzzleProps.offsets.Count) : ___burstShotsLeft % muzzleProps.offsets.Count;
                MuzzleFlashUtility.SpawnMuzzleFlash(caster.MapHeld, muzzleProps.def, drawPos, muzzleProps.offsets[index], direction, muzzleProps.drawSize, muzzleProps.useFlipped);
            }
            else
            {
                for (int i = 0; i < muzzleProps.offsets.Count; i++)
                {
                    MuzzleFlashUtility.SpawnMuzzleFlash(caster.MapHeld, muzzleProps.def, drawPos, muzzleProps.offsets[i], direction, muzzleProps.drawSize, muzzleProps.useFlipped);
                }
            }
        }
    }
}
