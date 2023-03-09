using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;

namespace MuzzleFlash
{
    public enum WeaponMode : int
    {
        Primary = 1,
        Secondary = 2,
        NoPatch = 4
    }

    public class MuzzleFlashProps : DefModExtension
    {
        public WeaponMode type = WeaponMode.Primary;
        public MuzzleFlashDef def = MuzzleFlashDefOf.MF_StandardMuzzleFalsh;
        public Vector2 drawSize = new Vector2(0.8f, 0.8f);
        public List<Vector2> offsets;
        public bool isAlternately;
        public bool useCenterAsRootPosition = false;
        public bool useFlipped = true;
    }
}
