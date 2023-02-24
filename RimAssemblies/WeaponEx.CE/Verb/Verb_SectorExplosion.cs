using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;
using CombatExtended;

namespace WeaponEx.CE
{
	public class Verb_SectorExplosion : Verb
	{
		public VerbProperties_SectorExplosion PorpsEx => verbProps as VerbProperties_SectorExplosion;
        private CompAmmoUser _ammoUser;
		public CompAmmoUser AmmoUser
        {
			get
			{
				if (_ammoUser == null) _ammoUser = EquipmentSource.GetComp<CompAmmoUser>();
				return _ammoUser;
			}
        }

		private bool IsAttacking
		{
			get
			{
				return ((CasterPawn != null) ? CasterPawn.CurJobDef : null) == JobDefOf.AttackStatic || base.WarmingUp;
			}
		}

		protected override int ShotsPerBurst => verbProps.burstShotCount;

		protected override bool TryCastShot()
		{
			if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
			{
				return false;
			}
			if(!TryComsumeAmmo()){
				return false;
				
			}

			ThrowFlame();
			

			return true;
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

			if(IsAttacking&&AmmoUser != null && !AmmoUser.CanBeFiredNow)
			{
				AmmoUser.TryStartReload();
				return false;
			}

			return true;
		}

		private bool TryComsumeAmmo()
		{
			if (AmmoUser == null) return true;
			if(!AmmoUser.CanBeFiredNow)
			{
				AmmoUser.TryStartReload();
				return false;
			}
			if(PorpsEx != null)
            {
				return AmmoUser.TryReduceAmmoCount(PorpsEx.ammoComsume);
			}
			return AmmoUser.TryReduceAmmoCount(1);
		}

		private void ThrowFlame()
		{
			IntVec3 position = caster.Position;

			float angle = Mathf.Atan2((float)(-(float)(currentTarget.Cell.z - position.z)), (float)(currentTarget.Cell.x - position.x)) * 57.29578f;
			float angleStart = angle - 12f;
			float angleEnd = angle + 12f;

			//FloatRange value = new FloatRange(angle - 12f, angle + 12f);
			//GenExplosion.DoExplosion(position, caster.MapHeld, verbProps.range, DamageDefOf.Flame, CasterPawn, 3, -1f, null, null, null, null, verbProps.spawnDef, 0.05f, 1, null, false, null, 0f, 1, 1f, false, null, null, new FloatRange?(value), false, 0.6f, 0f, false, null, 1f);

			ExplosionUtility.DoSectorExplosion(position, caster.MapHeld, verbProps.range, DamageDefOf.Flame, 3, CasterPawn, verbProps.spawnDef, angleStart, angleEnd);
			if (verbProps.sprayEffecterDef != null)
			{
				AddEffecterToMaintain(verbProps.sprayEffecterDef.Spawn(caster.Position, currentTarget.Cell, caster.Map, 1f), caster.Position, currentTarget.Cell, 14, caster.Map);
			}

			lastShotTick = Find.TickManager.TicksGame;
		}
	}
}
