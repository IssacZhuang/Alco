using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;

namespace ADS
{
    public class Verb_LaunchVisualProjectile : Verb
    {
        protected override bool TryCastShot()
        {
            return true;
        }
    }
}
