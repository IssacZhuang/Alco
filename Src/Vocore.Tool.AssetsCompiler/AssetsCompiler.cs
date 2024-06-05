using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Vocore.Tool.AssetsCompiler;

[Generator]
public class AssetsCompiler : ISourceGenerator
{
    public const string PrefixShader = "Shader_";
    public const string PrefixFont = "Font_";

    public static readonly Regex VariableNameRegex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

    public static readonly string GenFileName = "BuiltinAssets.g.cs";

    public static readonly string GenFileContentBegin = @"
// Auto generated code
using System;

namespace Vocore.Engine;

public static partial class BuiltInAssets{
    ";

    public static readonly string GenFileContentEnd = @"
}
";

    public void Execute(GeneratorExecutionContext context)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(GenFileContentBegin);
        foreach (AdditionalText file in context.AdditionalFiles)
        {
            if (ShoudGnerate(file, out string filename))
            {
                builder.AppendLine($"public static readonly string {filename} = @\"{file.Path}\";");
            }
        }

        builder.AppendLine(GenFileContentEnd);
        string code = builder.ToString();
        Debug.WriteLine(code);
        //context.AddSource(GenFileName, code);
    }

    public void Initialize(GeneratorInitializationContext context)
    {

    }

    private bool ShoudGnerate(AdditionalText file, out string filename)
    {
        string extension = Path.GetExtension(file.Path);
        string tmpFilename = Path.GetFileNameWithoutExtension(file.Path);


        switch (extension)
        {
            case ".hlsl":
                VerifyFileName(tmpFilename);
                filename = PrefixShader + tmpFilename;
                return true;
            case ".ttf":
            case ".otf":
                VerifyFileName(tmpFilename);
                filename = PrefixFont + tmpFilename;
                return true;
            default:
                filename = "";
                return false;
        }
    }



    private void VerifyFileName(string filename)
    {
        if (!VariableNameRegex.IsMatch(filename))
        {
            throw new Exception($"Invalid filename for built-in resource: {filename}, must match {VariableNameRegex}");
        }
    }
}
