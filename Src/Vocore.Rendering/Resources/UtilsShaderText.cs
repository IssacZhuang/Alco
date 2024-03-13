using System.Text;

namespace Vocore.Rendering;

/// <summary>
/// Utility class for preprocessing shader text. Can be used to the HLSL and GLSL shader text.
/// </summary>
public static class UtilsShaderText
{
    /// <summary>
    /// The key used to identify a pragma directive.
    /// </summary>
    public const string KeyPragma = "#pragma";
    public const string KeyInclude = "#include";
    public const string FormatLine = "#line {0} {1}"; // #line line filename

    /// <summary>
    /// Parses the shader text and retrieves all shader pragmas
    /// </summary>
    /// <param name="shaderText">The shader text to parse.</param>
    /// <returns>An array of <see cref="ShaderPragma"/> objects representing the shader pragmas.</returns>
    public static ShaderPragma[] GetShaderPragma(string shaderText)
    {
        //syntax: #pragma name value1 value2 value3 .. valueN
        List<ShaderPragma> shaderPragmas = new List<ShaderPragma>();
        using StringReader reader = new StringReader(shaderText);
        string? line;
        bool isInCommentBlock = false;
        while ((line = reader.ReadLine()) != null)
        {
            //remove spaces in the front
            line = line.TrimStart();
            // skip comments
            if (line.StartsWith("//"))
            {
                continue;
            }

            if (line.Contains("/*"))
            {
                isInCommentBlock = true;
            }

            if (line.Contains("*/"))
            {
                isInCommentBlock = false;
            }

            if (isInCommentBlock)
            {
                continue;
            }

            if (line.StartsWith(KeyPragma))
            {
                string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 1)
                {
                    ShaderPragma pragma = new ShaderPragma
                    {
                        Name = tokens[1],
                        Values = tokens.Skip(2).ToArray()
                    };
                    shaderPragmas.Add(pragma);
                }
            }
        }

        return shaderPragmas.ToArray();
    }

    /// <summary>
    /// Processes the shader text and resolves all include directives.
    /// </summary>
    /// <param name="shaderText">The shader text to process.</param>
    /// <param name="filename">The filename of the shader.</param>
    /// <param name="includeResolver">The include resolver function. input: shader name, return shader text</param>
    /// <returns>The processed shader text.</returns>
    public static string ProcessInclude(string shaderText, string filename, Func<string, string> includeResolver)
    {
        //syntax: #include "filename"
        using StringReader reader = new StringReader(shaderText);
        StringBuilder sb = new StringBuilder();
        string? line;
        bool isInCommentBlock = false;
        int lineCount = 0;
        while ((line = reader.ReadLine()) != null)
        {
            //remove spaces in the front
            line = line.TrimStart();
            // skip comments
            if (line.StartsWith("//"))
            {
                continue;
            }

            if (line.Contains("/*"))
            {
                isInCommentBlock = true;
            }

            if (line.Contains("*/"))
            {
                isInCommentBlock = false;
            }

            if (isInCommentBlock)
            {
                continue;
            }

            if (line.StartsWith(KeyInclude))
            {
                string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 1)
                {
                    string includeName = tokens[1].Trim('\"');
                    string includeText = includeResolver(includeName);
                    sb.AppendLine(string.Format(FormatLine, 0, includeName)); //#line 0 includeName
                    sb.AppendLine(includeText);
                    sb.AppendLine(string.Format(FormatLine, lineCount + 1, filename)); //#line lineCount+1 filename
                }
                else
                {
                    throw new InvalidOperationException($"Invalid include directive at line {lineCount} of {filename}");
                }
            }
            else
            {
                sb.AppendLine(line);
            }
            lineCount++;
        }

        return sb.ToString();
    }
}

