using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Vocore.Engine
{
    public static class ShaderPreProcessor
    {
        public const string regexPragma = @"#pragma\s+(\w+)\s+(\w+)";
        public static Dictionary<string, string> GetPragma(string shader)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string[] lines = shader.Split('\n');
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, regexPragma);
                if (match.Success)
                {
                    result.Add(match.Groups[1].Value, match.Groups[2].Value);
                }
            }
            return result;
        }

    }
}