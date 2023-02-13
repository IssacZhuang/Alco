using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;

namespace MuzzleFlash
{
    public class MuzzleFlashProps : DefModExtension
    {
        public MuzzleFlashDef def = MuzzleFlashDefOf.MF_StandardMuzzleFalsh;
        public Vector2 drawSize = new Vector2(0.8f, 0.8f);
        public List<Vector2> offsets;
        public bool isAlternately;
    }
}
