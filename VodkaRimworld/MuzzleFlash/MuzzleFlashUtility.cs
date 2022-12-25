using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;

namespace MuzzleFlash
{
    public static class MuzzleFlashUtility
    {
        public static void SpawnMuzzleFlash(this Map map, MuzzleFlashDef def, Vector3 drawLoc, Vector3 offset, Vector3 direction, Vector2 drawSize)
        {
            float angle = direction.AngleFlat();

            Vector3 drawPos = drawLoc + direction * (offset.x + drawSize.x * def.drawOffsetMultiplier.x) + Vector3.Cross(direction, Vector3.up).normalized * offset.y * MuzzleFlashUtility.GetFlipped(angle);
            drawPos.y = AltitudeLayer.VisEffects.AltitudeFor();

            MuzzleFlashEntity entity = new MuzzleFlashEntity(def, drawPos, angle, drawSize);

            map.GetComponent<MapComponent_MuzzleFlashManager>().RegisterEntity(entity);
        }

        public static float GetFlipped(float aimAngle)
        {
            if (aimAngle > 200f && aimAngle < 340f)
            {
                return -1f;
            }
            return 1f;
        }
    }
}
