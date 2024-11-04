using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vocore.Tool.AssetsCompiler;

[Generator]
public class GeneratorBuiltInAssetsPath : IIncrementalGenerator
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public static readonly string GenFileName = "BuiltinAssetsPath.gen.cs";

    public static readonly string GenFileContentBegin = @"
// Auto generated code
using System;

namespace Vocore.Engine;

public static partial class BuiltInAssetsPath
{
    ";

    public static readonly string GenFileContentEnd = @"
}
";

    public static readonly string GenStatementVariable = @"   public const string {0} = ""{1}"";";


    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var allFiles = context.AdditionalTextsProvider.Combine(context.AnalyzerConfigOptionsProvider).Collect();

        Dictionary<string, string> duplicateCheck = new Dictionary<string, string>();

        context.RegisterSourceOutput(allFiles, (context, files) =>
        {
            StringBuilder code = new StringBuilder();

            code.AppendLine(GenFileContentBegin);

            foreach (var file in files)
            {
                AdditionalText additionalText = file.Left;
                AnalyzerConfigOptions analyzerConfigOptions = file.Right.GlobalOptions;
                AnalyzerConfigOptions options = file.Right.GlobalOptions;
                string projectPath = options.GetMSBuildProperty("ProjectDir");

                string assetsPath = Path.Combine(projectPath, "Assets");

                if (options.TryGetValue("build_property.BuiltInAssetsPath", out string? customAssetsPath))
                {
                    assetsPath = Path.Combine(projectPath, customAssetsPath);
                }

                string filePath = additionalText.Path;
                string localPath = PathExtensions.GetRelativePath(assetsPath, filePath);
                localPath = FixWindowsPath(localPath);
                if (ShouldGenerate(filePath, out string namePrefix))
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string variableName = namePrefix + fileName;

                    if (!VariableNameRegex.IsMatch(fileName))
                    {
                        context.Warning($"Invalid variable name '{fileName}', should match regex '{VariableNameRegex}' in '{filePath}'. Skipped");
                        continue;
                    }

                    if (duplicateCheck.TryGetValue(variableName, out string? existingPath))
                    {
                        context.Warning($"Duplicate variable name '{variableName}' found in '{existingPath}' and '{filePath}'. Skipped");
                        continue;
                    }

                    duplicateCheck.Add(variableName, filePath);
                    //Warning(string.Format(GenStatementVariable, variableName, localPath));
                    code.AppendLine(string.Format(GenStatementVariable, variableName, localPath));
                    code.AppendLine();
                }
            }

            code.AppendLine(GenFileContentEnd);

            context.AddSource(GenFileName, code.ToString());


        });
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

    private string FixWindowsPath(string path)
    {
        return path.Replace("\\", "/");
    }
}