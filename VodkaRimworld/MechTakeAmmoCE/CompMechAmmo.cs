using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;
using UnityEngine;

using CombatExtended;
using Verse.AI;

namespace MTA
{
    public class CompMechAmmo:ThingComp
    {
        private Pawn _parentPawn;
        private Pawn_InventoryTracker _pawnInventory;
        private static Texture2D _gizmoIconSetMagCount;
        private static Texture2D _gizmoIconTakeAmmoNow;

        private readonly string _labelSetMagCount = "MTA_SetAmmoCount".Translate();
        private readonly string _labelTakeAmmoNow = "MTA_TakeAmmoNow".Translate();

        public static readonly int REFRESH_INTERVAL = 6000;

        public int magCount = 6;

        public Texture2D GizmoIcon_SetMagCount
        {
            get
            {
                if (_gizmoIconSetMagCount == null) _gizmoIconSetMagCount = ContentFinder<Texture2D>.Get(this.Props.gizmoIconSetMagCount, false);
                return _gizmoIconSetMagCount;
            }
        }

        public Texture2D GizmoIcon_TakeAmmoNow
        {
            get
            {
                if (_gizmoIconTakeAmmoNow == null) _gizmoIconTakeAmmoNow = ContentFinder<Texture2D>.Get(this.Props.gizmoIconTakeAmmoNow, false);
                return _gizmoIconTakeAmmoNow;
            }
        }

        public Pawn ParentPawn
        {
            get
            {
                if (_parentPawn == null) _parentPawn = this.parent as Pawn;
                return _parentPawn;
            }
        }

        public Pawn_InventoryTracker PawnInventory
        {
            get
            {
                if (_pawnInventory == null) _pawnInventory = ParentPawn?.inventory;
                return _pawnInventory;
            }
        }

        public CompProperties_MechAmmo Props => (CompProperties_MechAmmo)this.props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            yield return new Command_Action
            {
                action = SetMagCount,
                defaultLabel = _labelSetMagCount,
                //icon = GizmoIcon_SetMagCount,
            };
            yield return new Command_Action
            {
                action = TakeAmmoNow,
                defaultLabel = _labelTakeAmmoNow,
                //icon = GizmoIcon_TakeAmmoNow,
            };
            yield break;
        }

        public void SetMagCount()
        {

        }

        public void TakeAmmoNow()
        {
            if (ParentPawn == null) return;

            if (ParentPawn.Drafted) return;

            Log.Message("Try Take Ammo");
            this.TryTakeAmmoJob();
        }

        public void TryTakeAmmoJob()
        {
            if (ParentPawn == null) return;

            if (ParentPawn.CurJobDef == JobDefOf.MTA_TakeAmmo) return;

            if (ParentPawn.GetComp<CompMechAmmo>() == null) return;
            CompAmmoUser ammoUser = ParentPawn.equipment.Primary?.GetComp<CompAmmoUser>();

            if (ammoUser == null) return;

            AmmoDef currentAmmo = ammoUser.CurrentAmmo;

            Log.Message("Ammo Left:" + ammoUser.CurMagCount);

            if (ammoUser.CurMagCount >= ammoUser.MagSize * magCount) return;

            Thing ammoFound = ParentPawn.FindBestAmmo(currentAmmo);
            if (ammoFound == null) return;

            Job job = JobMaker.MakeJob(JobDefOf.MTA_TakeAmmo, ammoFound);
            ParentPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            ParentPawn.jobs.StartJob(job);
            Log.Message("Job issued");
        }
    }
}
