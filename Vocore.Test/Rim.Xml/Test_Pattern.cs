using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Vocore.Test.Rim.Xml
{
    
    public class Test_Pattern
    {
        //[Test("Text Pattern", false)]
        public void Test_Regrex(){
            string xPath = "Defs/ThingDef[defName=\"TMC_SR_MRAD\" or defName=\"PLA_QBZ\"]";
            //a regex to find the def type in the xPath (such as ThingDef, RecipeDef, etc.)
            string defTypeRegex = @"(?<=Defs/)\w+(?=\[)";
            //find the def type in the xPath using the defTypeRegex
            string defType = Regex.Match(xPath, defTypeRegex).Value; 
            //print
            Console.WriteLine(defType);
            //a regex to find the def name in the xPath (such as "TMC_SR_MRAD", "PLA_QBZ", etc.)
            string defNameRegex = @"defName=""([^""]+)""";
            //find the def name in the xPath using the defNameRegex and print all def name
            foreach (Match match in Regex.Matches(xPath, defNameRegex))
            {
                Console.WriteLine(match.Groups[1].Value);
            }
        }
    }
}
