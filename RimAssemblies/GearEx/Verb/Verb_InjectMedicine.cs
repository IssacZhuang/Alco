using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;

namespace GearEx
{
    public class Verb_InjectMedicine: Verb
    {
        protected override bool TryCastShot()
        {
            if (ReloadableCompSource == null||ReloadableCompSource.CanBeUsed)
            {
                WrapInjury();
                return true;
            }

            return false;
        }

        public override bool Available()
        {
            if (ReloadableCompSource != null && !ReloadableCompSource.CanBeUsed)
            {
                return false;
            }
            return base.Available();
        }

        public void WrapInjury()
        {
            Pawn wearer = CasterPawn;
            if(wearer == null)
            {
                return;
            }

            IEnumerable<Hediff> hediffs = wearer.health.hediffSet.hediffs;

            foreach (Hediff hediff in hediffs)
            {
                if (IsWrapableInjury(hediff))
                {
                    hediff.Tended(Rand.Range(0.8f, 1f), 1f);
                }
            }
        }

        private static bool IsWrapableInjury(Hediff hediff)
        {
            return hediff.TendableNow() && (hediff is Hediff_Injury || hediff is Hediff_MissingPart);
        }
    }
}

