using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Vocore.Tool.AssetsCompiler;

[Generator]
public class AssetsCompiler : ISourceGenerator
{


    public void Execute(GeneratorExecutionContext context)
    {
        BuiltInAssetsPathGenerator generator = new BuiltInAssetsPathGenerator(context);
        generator.Execute();

        BuiltInAssetsGenerator generator1 = new BuiltInAssetsGenerator(context);
        generator1.Execute();
    }

    public void Initialize(GeneratorInitializationContext context)
    {

    }


    private void VerifyFileName(string filename)
    {

    }
}
