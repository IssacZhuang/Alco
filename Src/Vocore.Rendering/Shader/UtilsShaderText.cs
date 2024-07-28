using System.Text;
using System.Text.RegularExpressions;

namespace Vocore.Rendering;

public static partial class UtilsShaderText
{
    /// <summary>
    /// Represents the key for the "#pragma" directive.
    /// </summary>
    public const string KeyPragma = "#pragma";

    [GeneratedRegex(@"\/\/.*|\/\*[\s\S]*?\*\/")]
    private static partial Regex MyRegex();

    public static string RemoveComments(string shaderText)
    {
        return MyRegex().Replace(shaderText, string.Empty);
    }

    public static ShaderPragma[] GetPragmas(string shaderText)
    {
        shaderText = RemoveComments(shaderText);
        string[] lines = shaderText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        List<ShaderPragma> pragmas = new();
        foreach (string line in lines)
        {
            if (TryGetPragma(line, out ShaderPragma pragma))
            {
                pragmas.Add(pragma);
            }
        }
        return pragmas.ToArray();
    }

    public static bool TryGetPragma(string shaderTextLine, out ShaderPragma pragma)
    {
        if (shaderTextLine.StartsWith(KeyPragma))
        {
            string[] tokens = shaderTextLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                pragma = new ShaderPragma
                {
                    Name = tokens[1],
                    Values = tokens.Skip(2).ToArray()
                };
                return true;
            }
        }

        pragma = default;
        return false;
    }


}