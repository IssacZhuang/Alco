using CppAst;

public class WebGPUBindingGenerator : BaseGenerator
{
    public override string OutputFolder => "Src/Alco.Graphics/WGPU/Bindings/Generated";

    public override void Generate()
    {
        ClearFolder();

        string outputPath = Path.Combine(SolutionFolder, OutputFolder);
        Directory.CreateDirectory(outputPath);

        string headersDir = Path.Combine(SolutionFolder, "WebGPUBindingGenerator", "headers");
        string webgpuHeader = Path.Combine(headersDir, "webgpu.h");
        string wgpuHeader = Path.Combine(headersDir, "wgpu.h");

        var parseOptions = new CppParserOptions
        {
            ParseMacros = true,
        };

        var compilation = CppParser.ParseFiles([webgpuHeader, wgpuHeader], parseOptions);

        foreach (CppDiagnosticMessage message in compilation.Diagnostics.Messages)
        {
            if (message.Type == CppLogMessageType.Error)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }

        var generateOptions = new CsCodeGeneratorOptions
        {
            OutputPath = outputPath,
            ClassName = "WebGPU",
            Namespace = "WebGPU",
            PublicVisiblity = false,
        };

        var generator = new CsCodeGenerator(generateOptions);
        generator.Generate(compilation);
    }
}
