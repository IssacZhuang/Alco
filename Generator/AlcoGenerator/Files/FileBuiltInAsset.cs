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
                code.AppendLine(string.Format(statement, variableName, localPath));
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
            case ".hlsl":
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
