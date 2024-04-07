using System.Text;

namespace Vocore.Rendering;


/// <summary>
/// Provides utility methods for working with shader text.
/// </summary>
public static class UtilsShaderText
{
    /// <summary>
    /// Represents the key for the "#pragma" directive.
    /// </summary>
    public const string KeyPragma = "#pragma";
    /// <summary>
    /// Represents the key used for including shader code.
    /// </summary>
    public const string KeyInclude = "#include";
    /// <summary>
    /// Represents the format string for a line directive in a shader file.
    /// </summary>
    public const string FormatLine = "#line {0} {1}"; // #line line filename

   
    /// <summary>
    /// Parses the given shader text and returns an array of shader pragmas.
    /// </summary>
    /// <param name="shaderText">The shader text to parse.</param>
    /// <returns>An array of <see cref="ShaderPragma"/> objects representing the shader pragmas found in the text.</returns>
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

            if (TryGetPragma(line, out ShaderPragma pragma))
            {
                shaderPragmas.Add(pragma);
            }
        }

        return shaderPragmas.ToArray();
    }

    /// <summary>
    /// Tries to extract a shader pragma from the given shader text line.
    /// </summary>
    /// <param name="shaderTextLine">The shader text line to parse.</param>
    /// <param name="pragma">When this method returns, contains the extracted shader pragma if successful; otherwise, the default value.</param>
    /// <returns><c>true</c> if a shader pragma was successfully extracted; otherwise, <c>false</c>.</returns>
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

    
    /// <summary>
    /// Processes the include directives in the given shader text and returns the processed shader text.
    /// </summary>
    /// <param name="shaderText">The original shader text.</param>
    /// <param name="filename">The name of the current file being processed.</param>
    /// <param name="includeResolver">A function that resolves the include directives.</param>
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

            if (TryGetInclude(line, filename, lineCount, includeResolver, out string processedLine))
            {
                sb.AppendLine(processedLine);
            }
            else
            {
                sb.AppendLine(line);
            }

            lineCount++;
        }

        return sb.ToString();
    }


    /// <summary>
    /// Tries to get the include text for a given shader text line.
    /// </summary>
    /// <param name="shaderTextLine">The shader text line.</param>
    /// <param name="filename">The filename.</param>
    /// <param name="lineCount">The line count.</param>
    /// <param name="includeResolver">The include resolver function.</param>
    /// <param name="processedLine">The processed line.</param>
    /// <returns><c>true</c> if the include text was successfully retrieved; otherwise, <c>false</c>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an invalid include directive is encountered.</exception>
    public static bool TryGetInclude(string shaderTextLine, string filename, int lineCount, Func<string, string> includeResolver, out string processedLine)
    {
        if (shaderTextLine.StartsWith(KeyInclude))
        {
            string[] tokens = shaderTextLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                string includeName = tokens[1].Trim('\"');
                string includeText = includeResolver(includeName);
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(string.Format(FormatLine, 0, includeName)); //#line 0 includeName
                builder.AppendLine(includeText);
                builder.AppendLine(string.Format(FormatLine, lineCount + 1, filename)); //#line lineCount+1 filename
                processedLine = builder.ToString();
                return true;
            }
            else
            {
                throw new InvalidOperationException($"Invalid include directive at line {lineCount} of {filename}");
            }
        }

        processedLine = shaderTextLine;
        return false;
    }
}