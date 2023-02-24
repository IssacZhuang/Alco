using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace WeaponEx.CE
{
    public class VerbProperties_SectorExplosion:VerbProperties
    {
        public int damageAmount = 3;
        public int ammoComsume = 1;
        public DamageDef damageDef;
    }

}

