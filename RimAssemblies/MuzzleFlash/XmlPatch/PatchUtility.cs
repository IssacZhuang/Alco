using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using Verse;

using MuzzleFlash;

namespace SafePatcher
{
    public static class PatchUtility
    {
        public static void ResolveDefs(Object obj, XmlNode sourceXml){
            //search all the fields of the object using the reflection
            foreach (var field in obj.GetType().GetFields())
            {
                //if the field is def or class extend of def, get the def from the defName
                if (field.FieldType == typeof(Def) || field.FieldType.IsSubclassOf(typeof(Def)))
                {
                    string defName = sourceXml[field.Name].InnerText;
                    Def def = GenDefDatabase.GetDef(field.FieldType, defName, true);
                    if (def == null)
                    {
                        Log.Error($"[SafePatcher] Def {defName} not found");
                    }
                    field.SetValue(obj, def);
                }
            }
        }
    }
}
