using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using DirectXShaderCompiler.NET;
using Vocore.Graphics;
using Vocore.ShaderCompiler;

namespace Vocore.Rendering;

 public static partial class UtilsShaderHLSL
{

    /// <summary>
    /// Represents the key for the "#define" directive.
    /// </summary>
    public const string DefineTrue = "1";

    /// <summary>
    /// Compiles the shader text with the specified filename and multi-compile defines.
    /// </summary>
    /// <param name="shaderText">The shader text to compile.</param>
    /// <param name="filename">The filename of the shader text.</param>
    /// <param name="multiCompileDefines">The multi-compile defines to use for the shader.</param>
    /// <param name="includeResolver">The function to resolve the include statements.</param>
    /// <returns>The compiled shader result.</returns>
    public static ShaderModulesInfo Compile(string shaderText, string filename, ReadOnlySpan<string> defines, FileIncludeHandler? includeResolver = null)
    {
        List<ShaderMacroDefine> macros = new List<ShaderMacroDefine>();

        for (int i = 0; i < defines.Length; i++)
        {
            macros.Add(new ShaderMacroDefine(defines[i], DefineTrue));
        }

        string[] defineArray = defines.ToArray();

        List<HlslFunctionInfo> functions = GetFunctionInfo(shaderText);
        ShaderStage stage = ShaderStage.None;
        foreach (HlslFunctionInfo function in functions)
        {
            stage |= function.Stage;
        }

        if (stage == ShaderStage.None)
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }

        if(stage.HasFlag(ShaderStage.Vertex) && !stage.HasFlag(ShaderStage.Fragment))
        {
            throw new ShaderValidationException("Missing pixel entry point in the shader.");
        }

        if (!stage.HasFlag(ShaderStage.Vertex) && stage.HasFlag(ShaderStage.Fragment))
        {
            throw new ShaderValidationException("Missing vertex entry point in the shader.");
        }

        //check if compute shader is in the same file with vertex or fragment shader
        if (stage.HasFlag(ShaderStage.Compute) && (stage.HasFlag(ShaderStage.Vertex) || stage.HasFlag(ShaderStage.Fragment)))
        {
            throw new ShaderValidationException("No vertex or fragment entry point is allowed in the compute shader.");
        }

        if (stage.HasFlag(ShaderStage.Vertex) && stage.HasFlag(ShaderStage.Fragment))
        {
            

            ShaderModule vertex = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Vertex,
                ShaderEntry.Vertex,
                filename,
                macros.ToArray(),
                includeResolver
                );

            ShaderModule pixel = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Fragment,
                ShaderEntry.Fragment,
                filename,
                macros.ToArray(),
                includeResolver
                );

            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(vertex.Source, pixel.Source, true);
            ShaderModulesInfo modulesInfo = ShaderModulesInfo.CreateGraphics(
                filename,
                defineArray,
                vertex,
                pixel,
                reflectionInfo
                );

            return modulesInfo;
        }else if(stage.HasFlag(ShaderStage.Compute))
        {
            

            //add shader modules with zero defines
            ShaderModule compute = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Compute,
                ShaderEntry.Compute,
                filename,
                macros.ToArray()
                );

            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(compute.Source, true);
            ShaderModulesInfo modulesInfo = ShaderModulesInfo.CreateCompute(
                filename,
                defineArray,
                compute,
                reflectionInfo
                );
            
            return modulesInfo;
        }
        else
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }
    }

    public static readonly Regex RegexFunction = new Regex(@"(\[[^]]*\]\s*)*\s*(\w+)\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

    public static List<HlslFunctionInfo> GetFunctionInfo(string code)
    {
        var functions = new List<HlslFunctionInfo>();
        //var functionPattern = new Regex(@"(\[\s*shader\s*\(\s*""\w+""\s*\)\s*\])*\s*(\w+)\s+(\w+)\s*\(([^)]*)\)\s*{", RegexOptions.Compiled);
        
        var matches = RegexFunction.Matches(code);
        foreach (Match match in matches)
        {
            List<string> attrs = new List<string>();

            var attributes = match.Groups[1].Captures;
            foreach (Capture attribute in attributes)
            {
                attrs.Add(attribute.Value.Trim());
            }

            var functionInfo = new HlslFunctionInfo(
                match.Groups[2].Value,
                match.Groups[3].Value,
                match.Groups[4].Value,
                attrs.ToArray()
                );

            functions.Add(functionInfo);
        }

        return functions;
    }

    private static string NoIncludeResolver(string includeName)
    {
        throw new InvalidOperationException($"Include statement found in the shader but no include resolver is provided.");
    }
}