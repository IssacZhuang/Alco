using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace ADS
{
    public static class SkyfallerUtility
    {
        public static float GetHeight(this Skyfaller skyfaller)
        {
            return skyfaller.DrawPos.z - skyfaller.Position.z;
        }

        public static bool HasThreat(this Skyfaller skyfaller, Faction toFaction)
        {
            foreach(Thing thing in skyfaller.innerContainer)
            {
                if (!(thing is Pawn pawn)) continue;
                if (pawn.Dead || pawn.Downed || pawn.IsPrisoner|| pawn.IsSlave) continue;
                if (pawn.HostFaction.HostileTo(toFaction)) return true;
            }
            return false;
        }

        public static bool InRangeOfTurret(this Skyfaller skyfaller, CompTurretAirDefense turret)
        {
            return skyfaller.Position.DistanceTo(turret.parent.Position) <= turret.Props.range;
        }

        public static void RegiserToMap(this Skyfaller skyfaller, Map map)
        {
            map.GetComponent<MapCompent_AirDefenseManager>()?.RegisterSkyfaller(skyfaller);
        }
    }
}
