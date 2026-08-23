using System.Text;
using System.Text.RegularExpressions;

public class FileBuiltInAssetPath
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    private static readonly string GenFileContentBegin = @"
// Auto generated code
using System;

namespace Alco.Engine;

public static partial class BuiltInAssetsPath
{
    ";

    private static readonly string GenFileContentEnd = @"
}
";

    private static readonly string GenStatementVariable = @"   public const string {0} = ""{1}"";";

    private readonly List<(FileInfo File, string RelativePath)> _files;
    private readonly Dictionary<string, string> _duplicateCheck = new Dictionary<string, string>();

    public FileBuiltInAssetPath(List<(FileInfo File, string RelativePath)> files)
    {
        _files = files;
    }

    public string GenerateContent()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(GenFileContentBegin);

        foreach (var (file, localPath) in _files)
        {
            string filePath = file.FullName;

            if (ShouldGenerate(filePath, out string namePrefix))
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
                string variableName = namePrefix + FileBuiltInAsset.ToPascalIdentifier(fileName);

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
                builder.AppendLine(string.Format(GenStatementVariable, variableName, value));
                builder.AppendLine();
            }
        }

        builder.AppendLine(GenFileContentEnd);
        return builder.ToString();
    }

    private bool ShouldGenerate(string filePath, out string namePrefix)
    {
        string extension = Path.GetExtension(filePath);
        switch (extension)
        {
            case ".ttf":
                namePrefix = PrefixFont;
                return true;
            case ".slang":
                namePrefix = PrefixShader;
                return true;
            default:
                namePrefix = string.Empty;
                return false;
        }
    }
}
