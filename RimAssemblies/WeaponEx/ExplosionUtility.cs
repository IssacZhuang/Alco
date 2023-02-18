using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;

namespace WeaponEx
{
    public static class ExplosionUtility
    {
        public static void DoSectorExplosion(IntVec3 position, Map map, float range, DamageDef damageDef, int damageAmount, Pawn instigator, ThingDef postSpawnDef, float angleStart, float angleEnd)
        {
            GenExplosion.DoExplosion(position, map, range, damageDef, instigator, damageAmount, -1f, null, null, null, null, postSpawnDef, 0.05f, 1, null, false, null, 0f, 1, 1f, false, null, null, new FloatRange(angleStart, angleEnd), false, 0.6f, 0f, false, null, 1f);

            if (angleEnd > 180)
            {
                angleEnd -= -360;
                angleStart = -180;
                GenExplosion.DoExplosion(position, map, range, damageDef, instigator, damageAmount, -1f, null, null, null, null, postSpawnDef, 0.05f, 1, null, false, null, 0f, 1, 1f, false, null, null, new FloatRange(angleStart, angleEnd), false, 0.6f, 0f, false, null, 1f);
            }

            if (angleStart < -180)
            {
                angleStart += 360;
                angleEnd = 180;
                GenExplosion.DoExplosion(position, map, range, damageDef, instigator, damageAmount, -1f, null, null, null, null, postSpawnDef, 0.05f, 1, null, false, null, 0f, 1, 1f, false, null, null, new FloatRange(angleStart, angleEnd), false, 0.6f, 0f, false, null, 1f);
            }
        }
    }
}
