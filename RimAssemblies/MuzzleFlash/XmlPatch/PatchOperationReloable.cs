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
        string regexDefName = @"defName=""([^""]+)""";

        public void ReloadPatch(){
            string defType = Regex.Match(xpath, regexDefType).Value; 
            List<string> defNames = new List<string>();
            foreach (Match match in Regex.Matches(xpath, regexDefName))
            {
                defNames.Add(match.Groups[1].Value);
            }

            //get type from defType
            Type type = Type.GetType(defType);
            List<Def> defs = DefDatabase<Def>.AllDefsListForReading.Where(d => d.GetType() == type).ToList();
            foreach (Def def in defs)
            {
                if (defNames.Contains(def.defName))
                {
                    ApplyToObject(def);
                }
            }
            //contact def name to a string with comma
            string defNameString = string.Join(",", defNames);
            //print
            Log.Message($"[SafePatcher] Patches reloaded for {defType} - {defNameString}");
        }

        public virtual void ApplyToObject(Def def){
            

        }
    }
}
