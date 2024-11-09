using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Vocore.Rendering;

public class IncludeHelper
{
    public const string KeyInclude = "#include"; // #include "Base.hlsli"
    public const string FormatLine = "#line {0} \"{1}\""; // #line line filename
    public const int MaxRecursionDepth = 32;

    private readonly StringBuilder _builder = new StringBuilder();
    private readonly HashSet<string> _includedFiles = new HashSet<string>();

    /// <summary>
    /// Processes the include statements in the shader text.
    /// </summary>
    /// <param name="shaderText">The shader text to process.</param>
    /// <param name="filename">The filename of the shader text.</param>
    /// <param name="includeResolver">The function to resolve the include statements.</param>
    /// <returns>The shader text with the include statements resolved.</returns>
    public string ProcessInclude(string shaderText, string filename, Func<string, string> includeResolver, int depth = 0)
    {
        _builder.Clear();
        _includedFiles.Clear();
        
        if (depth > MaxRecursionDepth)
        {
            throw new InvalidOperationException($"Include recursion depth exceeds the maximum depth of {MaxRecursionDepth}. It might be looping include in the file.");
        }

        using StringReader reader = new StringReader(shaderText);
        string? line;
        int lineCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineCount++;
            line = line.TrimStart();

            if (TryGetInclude(line, includeResolver, out string? includedText, out string? includeFilename) &&
                !_includedFiles.Contains(includeFilename))
            {
                _includedFiles.Add(includeFilename);

                includedText = ProcessInclude(includedText, includeFilename, includeResolver, depth + 1);

                _builder.AppendLine(string.Format(FormatLine, 1, includeFilename));
                _builder.AppendLine(includedText);
                _builder.AppendLine(string.Format(FormatLine, lineCount + 1, filename));
            }
            else
            {
                _builder.AppendLine(line);
            }
        }

        return _builder.ToString();
    }

    private static bool TryGetInclude(string shaderTextLine, Func<string, string> includeResolver, [NotNullWhen(true)] out string? includedText, [NotNullWhen(true)] out string? includedFilename)
    {
        if (shaderTextLine.StartsWith(KeyInclude))
        {
            string[] tokens = shaderTextLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                string includeName = tokens[1].Trim('\"');
                try
                {
                    includedText = includeResolver(includeName);
                    includedFilename = includeName;
                    return true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error resolving include statement: {shaderTextLine}", ex);
                }
            }
            else
            {
                throw new InvalidOperationException($"Invalid include statement: {shaderTextLine}");
            }
        }

        includedText = null;
        includedFilename = null;
        return false;
    }
}