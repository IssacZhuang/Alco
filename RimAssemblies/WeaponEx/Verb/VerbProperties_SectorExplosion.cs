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
    public class VerbProperties_SectorExplosion:VerbProperties
    {
        public int damageAmount = 3;
        public DamageDef damageDef;
    }

}

