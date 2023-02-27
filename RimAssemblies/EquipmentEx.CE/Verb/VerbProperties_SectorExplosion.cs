using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using Verse.Sound;
using RimWorld;
using UnityEngine;

namespace EquipmentEx.CE
{
    public class VerbProperties_SectorExplosion:VerbProperties
    {
        public int damageAmount = 3;
        public int ammoComsume = 1;
        public DamageDef damageDef;
        public float angle = 20f; 
    }

}

