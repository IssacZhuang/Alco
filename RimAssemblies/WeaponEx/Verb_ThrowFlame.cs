using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace WeaponEx
{
    public class Verb_ThrowFlame : Verb
    {
        protected override int ShotsPerBurst => verbProps.burstShotCount;

        protected override bool TryCastShot()
        {
			if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
			{
				return false;
			}
			if (EquipmentSource != null)
			{
				CompChangeableProjectile comp = EquipmentSource.GetComp<CompChangeableProjectile>();
				if (comp != null)
				{
					comp.Notify_ProjectileLaunched();
				}
				CompReloadable comp2 = EquipmentSource.GetComp<CompReloadable>();
				if (comp2 != null)
				{
					comp2.UsedOnce();
				}
			}
			Log.Message("burst");

			ThrowFlame();

			return true;
		}

        public override void WarmupComplete()
        {
            base.WarmupComplete();
        }

        public override bool Available()
		{
			if (!base.Available())
			{
				return false;
			}
			if (CasterIsPawn)
			{
				Pawn casterPawn = CasterPawn;
				if (casterPawn.Faction != Faction.OfPlayer && casterPawn.mindState.MeleeThreatStillThreat && casterPawn.mindState.meleeThreat.Position.AdjacentTo8WayOrInside(casterPawn.Position))
				{
					return false;
				}
			}
			return true;
		}

		private void ThrowFlame()
        {
			IntVec3 position = caster.Position;

			float num = Mathf.Atan2((float)(-(float)(currentTarget.Cell.z - position.z)), (float)(currentTarget.Cell.x - position.x)) * 57.29578f;

			FloatRange value = new FloatRange(num - 10f, num + 10f);

			GenExplosion.DoExplosion(position, caster.MapHeld, verbProps.range, DamageDefOf.Flame, CasterPawn, 3, -1f, null, null, null, null, verbProps.spawnDef, 0.1f, 1, null, false, null, 0f, 1, 1f, false, null, null, new FloatRange?(value), false, 0.6f, 0f, false, null, 1f);
			if(verbProps.sprayEffecterDef != null)
            {
				AddEffecterToMaintain(verbProps.sprayEffecterDef.Spawn(caster.Position, currentTarget.Cell, caster.Map, 1f), caster.Position, currentTarget.Cell, 14, caster.Map);
			}
			
			lastShotTick = Find.TickManager.TicksGame;
		}
	}
}
