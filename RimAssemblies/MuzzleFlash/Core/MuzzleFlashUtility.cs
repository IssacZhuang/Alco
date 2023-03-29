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
        public static void SpawnMuzzleFlash(this Map map, MuzzleFlashDef def, Vector3 drawLoc, Vector3 offset, Vector3 direction, Vector2 drawSize, bool useFlipped = true)
        {
            float angle = direction.AngleFlat();

            Vector3 offsetY = Vector3.Cross(direction, Vector3.up).normalized * offset.y;

            if (useFlipped)
            {
                offsetY *= MuzzleFlashUtility.GetFlipped(angle);
            }

            Vector3 drawPos = drawLoc + direction * (offset.x + drawSize.x * def.drawOffsetMultiplier.x) + offsetY;

            drawPos.y = AltitudeLayer.VisEffects.AltitudeFor();

            MuzzleFlashEntity entity = new MuzzleFlashEntity(def, drawPos, angle, drawSize);

            map.GetComponent<MapComponent_MuzzleFlashManager>().RegisterEntity(entity);
        }

        /// <summary>
        /// Spawn muzzle flash based on the given verb and custom barrel index
        /// </summary>
        public static void SpawnMuzzleFlashByVerbIndex(this Verb verb, int index, bool useFlipped = true)
        {
            Thing caster = verb.Caster;
            if (caster == null) return;
            if (!(verb.DirectOwner is ThingComp)) return;

            Thing equipment = verb.EquipmentSource;

            if (equipment == null) return;

            if (!MuzzleFlashUtility.MuzzleFlashAvailable(verb)) return;

            MuzzleFlashProps primary = null;
            MuzzleFlashProps secondary = null;
            equipment?.def?.GetMuzzleFlashProps(out primary, out secondary);

            MuzzleFlashProps muzzleProps = verb.IsPrimaryVerb() ? primary : secondary;
            if (muzzleProps == null || muzzleProps.offsets == null || muzzleProps.offsets.NullOrEmpty()) return;

            Vector3 targetPosition = verb.CurrentTarget.CenterVector3;
            Vector3 sourcePostion = caster.DrawPos;
            Vector3 direction = (targetPosition - sourcePostion);
            Vector3 drawPos;

            if (verb.CasterIsPawn && !muzzleProps.useCenterAsRootPosition)
            {
                drawPos = WeaponPositionCache.GetDrawPos(equipment);
            }
            else
            {
                drawPos = sourcePostion;
            }
            direction.y = 0;
            direction.Normalize();


            index = index % muzzleProps.offsets.Count;
            MuzzleFlashUtility.SpawnMuzzleFlash(caster.MapHeld, muzzleProps.def, drawPos, muzzleProps.offsets[index], direction, muzzleProps.drawSize, useFlipped);
        }


        public static float GetFlipped(float aimAngle)
        {
            if (aimAngle > 200f && aimAngle < 340f)
            {
                return -1f;
            }
            return 1f;
        }

        public static bool MuzzleFlashAvailable(this Verb verb)
        {
            if (verb == null) return false;
            return verb.EquipmentCompSource?.PrimaryVerb == verb;
        }

        public static bool IsPrimaryVerb(this Verb verb)
        {
            if (verb == null) return false;
            return verb.EquipmentCompSource?.parent?.def?.Verbs?.FirstOrDefault() == verb.verbProps;
        }

        public static void GetMuzzleFlashProps(this ThingDef def, out MuzzleFlashProps primary, out MuzzleFlashProps secondary)
        {
            primary = null;
            secondary = null;
            if (def == null) return;
            if (def.modExtensions == null)
            {
                return;
            }
            for (int i = 0; i < def.modExtensions.Count; i++)
            {
                if (!(def.modExtensions[i] is MuzzleFlashProps props))
                {
                    continue;
                }
                
                if(props.type.HasFlag(WeaponMode.Primary))
                {
                    primary = props;
                }
                
                if (props.type.HasFlag(WeaponMode.Secondary))
                {
                    secondary = props;
                }
            }

        }
    }
}
