using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using System.Xml;
using Verse;

namespace SafePatcher
{
    public class PatchOperationReloable: PatchOperationPathed
    {
        public static string regexDefType = @"(?<=Defs/)\w+(?=\[)";
        public static string regexDefName = @"defName=""([^""]+)""";
        protected override bool ApplyWorker(XmlDocument xml)
        {
            return true;
        }

        public void ReloadPatch(){
            string defType = Regex.Match(xpath, regexDefType).Value; 
            List<string> defNames = new List<string>();
            foreach (Match match in Regex.Matches(xpath, regexDefName))
            {
                defNames.Add(match.Groups[1].Value);
            }

            //get type from defType
            Type type = Type.GetType(defType);
            //List<Def> defs = DefDatabase<Def>.AllDefsListForReading.Where(d => d.GetType() == type).ToList();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            foreach (Def def in defs)
            {
                if (defNames.Contains(def.defName))
                {
                    try
                    {
                        ApplyToObject(def);
                        Log.Message($"[SafePatcher] Reloaded patch for {def.defName}");
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[SafePatcher] Error while reloading patch for {def.defName} - {e.Message}");
                    }
                }
            }

        }

        public virtual void ApplyToObject(Def def){
           

        }
    }
}
