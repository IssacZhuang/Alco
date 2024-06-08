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
        BuiltInAssetLinkGenerator generator = new BuiltInAssetLinkGenerator(context);
        generator.Execute();
    }

    public void Initialize(GeneratorInitializationContext context)
    {

    }


    private void VerifyFileName(string filename)
    {

    }
}
