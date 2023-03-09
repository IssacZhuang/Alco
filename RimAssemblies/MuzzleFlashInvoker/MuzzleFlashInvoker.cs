using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;

namespace MuzzleFlashInvoker
{
    public static class MuzzleFlashInvoker
    {
        const string MuzzleFlashUtility = "MuzzleFlash.MuzzleFlashUtility";
        const string SpawnMuzzleFlashByVerbIndex = "SpawnMuzzleFlashByVerbIndex";
        private static MethodInfo _funcSpawnMuzzleFlash;

        private static bool _initialized = false;

        public static void SpawnMuzzleFlash(this Verb verb, int index, bool useFlipped = true){
            Initialize();
            if (_funcSpawnMuzzleFlash == null) return;
            _funcSpawnMuzzleFlash.Invoke(null, new object[] { verb, index, useFlipped });
        }

        private static void Initialize(){
            if (_initialized) return;
            _initialized = true;
            Assembly assemblyMuzzleFlash = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MuzzleFlash");
            if (assemblyMuzzleFlash == null) return;
            Type typeMuzzleFlashUtility = assemblyMuzzleFlash.GetType(MuzzleFlashUtility);
            if (typeMuzzleFlashUtility == null) return;
            _funcSpawnMuzzleFlash = typeMuzzleFlashUtility.GetMethod(SpawnMuzzleFlashByVerbIndex, BindingFlags.Public | BindingFlags.Static);
        }
    }
}
