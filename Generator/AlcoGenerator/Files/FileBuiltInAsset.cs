using System.Text;
using System.Text.RegularExpressions;

public class FileBuiltInAsset
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    private static readonly string GenFileContentBegin = @"
// Auto generated code
using System;
using Alco.IO;
using Alco.GUI;
using Alco.Audio;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public partial class BuiltInAssets
{
    ";

    private static readonly string GenFileContentEnd = @"
}
";

    private static readonly string GenStatementShader = @"    public Shader {0} => GetShader(""{1}"");";
    private static readonly string GenStatementFont = @"    public Font {0} => GetFont(""{1}"");";

    private readonly List<(FileInfo File, string RelativePath)> _files;
    private readonly Dictionary<string, string> _duplicateCheck = new Dictionary<string, string>();

    /// <summary>Turns a kebab/snake asset stem into a PascalCase identifier
    /// ('gaussian-blur-rgba16f' → 'GaussianBlurRgba16f').</summary>
    public static string ToPascalIdentifier(string stem) =>
        string.Join(string.Empty, stem
            .Split('-', '_')
            .Select(word => word.Length == 0 ? word : char.ToUpper(word[0]) + word[1..]));

    public FileBuiltInAsset(List<(FileInfo File, string RelativePath)> files)
    {
        _files = files;
    }

    public string GenerateContent()
    {
        StringBuilder code = new StringBuilder();
        code.AppendLine(GenFileContentBegin);

        foreach (var (file, localPath) in _files)
        {
            string filePath = file.FullName;

            if (ShouldGenerate(filePath, out string namePrefix, out string statement))
            {
                // Import-only shader libraries own no entry points; loading them
                // as shaders would fail to link, so no accessors are generated.
                if (Path.GetExtension(filePath) == ".slang" && localPath.Contains("ShadersSlang/Libs/"))
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(filePath);
                // Asset stems are kebab-case (slang convention); the generated
                // identifier PascalCases each dashed word (fxaa → Fxaa).
                string variableName = namePrefix + ToPascalIdentifier(fileName);

                if (!VariableNameRegex.IsMatch(variableName))
                {
                    Console.WriteLine($"Warning: Invalid variable name '{variableName}' in '{filePath}'. Skipped");
                    continue;
                }

                if (_duplicateCheck.TryGetValue(variableName, out string? existingPath))
                {
                    Console.WriteLine($"Warning: Duplicate variable name '{variableName}' found in '{existingPath}' and '{filePath}'. Skipped");
                    continue;
                }

                _duplicateCheck.Add(variableName, filePath);
                string value = Path.GetExtension(filePath) == ".slang"
                    ? fileName.Replace('_', '-')
                    : localPath;
                code.AppendLine(string.Format(statement, variableName, value));
                code.AppendLine();
            }
        }

        code.AppendLine(GenFileContentEnd);
        return code.ToString();
    }

    private bool ShouldGenerate(string filePath, out string namePrefix, out string statement)
    {
        string extension = Path.GetExtension(filePath);
        switch (extension)
        {
            case ".ttf":
                statement = GenStatementFont;
                namePrefix = PrefixFont;
                return true;
            case ".slang":
                statement = GenStatementShader;
                namePrefix = PrefixShader;
                return true;
            default:
                statement = string.Empty;
                namePrefix = string.Empty;
                return false;
        }
    }
}
