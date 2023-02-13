using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using Verse.AI;
using RimWorld;
using CombatExtended;

namespace MTA
{
    public static class Toils_Ammo
    {
        public static Toil Drop(ThingDef def, int count)
        {
            Toil toil = ToilMaker.MakeToil("DropAmmo");
            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                actor.inventory.DropCount(def, count);
            };
            return toil;
        }

        // public static Toil TryReloadAmmo(CompAmmoUser ammoUser)
        // {
        //     Toil toil = ToilMaker.MakeToil("TryReloadAmmo");
        //     toil.initAction = () =>
        //     {
        //         if (ammoUser == null) return;
        //         if (ammoUser.FullMagazine) return;
        //         ammoUser.TryUnload();
        //         ammoUser.TryStartReload();
        //     };
        //     return toil;
        // }
    }
}
