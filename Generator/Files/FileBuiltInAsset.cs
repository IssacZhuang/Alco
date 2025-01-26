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

    private readonly List<FileInfo> _files;
    private readonly Dictionary<string, string> _duplicateCheck = new Dictionary<string, string>();
    private readonly string _assetsPath;

    public FileBuiltInAsset(List<FileInfo> files, string assetsPath)
    {
        _files = files;
        _assetsPath = assetsPath;
    }

    public string GenerateContent()
    {
        StringBuilder code = new StringBuilder();
        code.AppendLine(GenFileContentBegin);

        foreach (var file in _files)
        {
            string filePath = file.FullName;
            string localPath = GetLocalPath(filePath);

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

    private string GetLocalPath(string filePath)
    {
        string relativePath = Path.GetRelativePath(_assetsPath, filePath);
        return relativePath.Replace("\\", "/");
    }
}