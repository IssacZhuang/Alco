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
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string variableName = namePrefix + fileName;

                if (!VariableNameRegex.IsMatch(fileName))
                {
                    Console.WriteLine($"Warning: Invalid variable name '{fileName}', should match regex '{VariableNameRegex}' in '{filePath}'. Skipped");
                    continue;
                }

                if (_duplicateCheck.TryGetValue(variableName, out string? existingPath))
                {
                    Console.WriteLine($"Warning: Duplicate variable name '{variableName}' found in '{existingPath}' and '{filePath}'. Skipped");
                    continue;
                }

                _duplicateCheck.Add(variableName, filePath);
                builder.AppendLine(string.Format(GenStatementVariable, variableName, localPath));
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
            case ".hlsl":
                namePrefix = PrefixShader;
                return true;
            default:
                namePrefix = string.Empty;
                return false;
        }
    }
}
