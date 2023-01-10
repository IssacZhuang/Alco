using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;
using CombatExtended;

namespace MTA
{
    public static class AmmoUtility
    {
        public static Thing FindBestAmmo(this Pawn pawn, ThingDef ammoDef)
        {
            if (pawn == null || ammoDef == null) return null;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(ammoDef), PathEndMode.ClosestTouch, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false, false, false), 9999f, null, null, 0, -1, false, RegionType.Set_Passable, false);
        }

        public static void TryTakeAmmoJob(this Pawn pawn)
        {
            Log.Message("Pawn Check");
            if (pawn == null) return;

            Log.Message("Job Def Check");
            if (pawn.CurJobDef == JobDefOf.MTA_TakeAmmo) return;

            Log.Message("CompMechAmmo Check");
            if (pawn.GetComp<CompMechAmmo>() == null) return;
            CompAmmoUser ammoUser = pawn.equipment.Primary.GetComp<CompAmmoUser>();

            Log.Message("CompAmmoUser Check");
            if (ammoUser == null) return;

            AmmoDef currentAmmo = ammoUser.CurrentAmmo;

            Log.Message("Ammo Search");
            Thing ammoFound = pawn.FindBestAmmo(currentAmmo);
            if (ammoFound == null) return;

            Job job = JobMaker.MakeJob(JobDefOf.MTA_TakeAmmo, ammoFound);
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            pawn.jobs.StartJob(job);
            Log.Message("Job issued");
        }
    }
}
