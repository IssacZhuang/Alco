using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using DirectXShaderCompiler.NET;
using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

public static partial class ShaderUtility
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
    public static ShaderModulesInfo CompileHLSL(string shaderText, string filename, ReadOnlySpan<string> defines, FileIncludeHandler? includeResolver = null)
    {
        List<ShaderMacroDefine> macros = new List<ShaderMacroDefine>();

        for (int i = 0; i < defines.Length; i++)
        {
            macros.Add(new ShaderMacroDefine(defines[i], DefineTrue));
        }

        string[] defineArray = defines.ToArray();

        List<HlslFunctionInfo> functions = GetHLSLFunctionInfo(shaderText);
        HlslFunctionInfo? functionVertex = null;
        HlslFunctionInfo? functionPixel = null;
        HlslFunctionInfo? functionCompute = null;
        ShaderStage stage = ShaderStage.None;
        foreach (HlslFunctionInfo function in functions)
        {
            stage |= function.Stage;
            if (function.Stage.HasFlag(ShaderStage.Vertex))
            {
                functionVertex = function;
            }
            if (function.Stage.HasFlag(ShaderStage.Fragment))
            {
                functionPixel = function;
            }
            if (function.Stage.HasFlag(ShaderStage.Compute))
            {
                functionCompute = function;
            }
        }

        if (stage == ShaderStage.None)
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }

        if (stage.HasFlag(ShaderStage.Vertex) && !stage.HasFlag(ShaderStage.Fragment))
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
                functionVertex!.Name,
                filename,
                macros.ToArray(),
                includeResolver
                );

            ShaderModule pixel = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Fragment,
                functionPixel!.Name,
                filename,
                macros.ToArray(),
                includeResolver
                );

            ShaderReflectionInfo reflectionInfo = ShaderRelfectionUtility.GetSpirvReflection(vertex.Source, pixel.Source, true);
            ShaderModulesInfo modulesInfo = ShaderModulesInfo.CreateGraphics(
                filename,
                defineArray,
                vertex,
                pixel,
                reflectionInfo
                );

            return modulesInfo;
        }
        else if (stage.HasFlag(ShaderStage.Compute))
        {


            //add shader modules with zero defines
            ShaderModule compute = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Compute,
                functionCompute!.Name,
                filename,
                macros.ToArray()
                );

            ShaderReflectionInfo reflectionInfo = ShaderRelfectionUtility.GetSpirvReflection(compute.Source, true);
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

    public static List<HlslFunctionInfo> GetHLSLFunctionInfo(string code)
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

    public static ReadOnlyMemory<byte> EncodeShaderModule(ShaderModule shaderModule)
    {
        BinaryTable table = new BinaryTable();
        table.Add(nameof(shaderModule.Stage), shaderModule.Stage);
        table.Add(nameof(shaderModule.Language), shaderModule.Language);
        table.Add(nameof(shaderModule.Source), shaderModule.Source);
        table.Add(nameof(shaderModule.EntryPoint), shaderModule.EntryPoint);
        return BinaryParser.EncodeTable(table);
    }

    public static ShaderModule DecodeShaderModule(ReadOnlySpan<byte> data)
    {
        BinaryTable table = BinaryParser.DecodeTable(data);
        return new ShaderModule(
            table.GetEnum<ShaderStage>(nameof(ShaderModule.Stage)), 
            table.GetEnum<ShaderLanguage>(nameof(ShaderModule.Language)), 
            table.GetBinary(nameof(ShaderModule.Source)), 
            table.GetString(nameof(ShaderModule.EntryPoint)));
    }

    public static ReadOnlyMemory<byte> EncodeShaderModulesInfo(ShaderModulesInfo modulesInfo)
    {
        BinaryTable table = new BinaryTable();
        table.Add(nameof(modulesInfo.Name), modulesInfo.Name);

        // Create a BinaryArray for the defines
        BinaryArray definesArray = new BinaryArray();
        foreach (string define in modulesInfo.Defines)
        {
            definesArray.Add(define);
        }
        table.Add(nameof(modulesInfo.Defines), definesArray);

        if (modulesInfo.VertexShader.HasValue)
        {
            table.Add(nameof(modulesInfo.VertexShader), EncodeShaderModule(modulesInfo.VertexShader.Value));
        }

        if (modulesInfo.FragmentShader.HasValue)
        {
            table.Add(nameof(modulesInfo.FragmentShader), EncodeShaderModule(modulesInfo.FragmentShader.Value));
        }

        if (modulesInfo.ComputeShader.HasValue)
        {
            table.Add(nameof(modulesInfo.ComputeShader), EncodeShaderModule(modulesInfo.ComputeShader.Value));
        }

        return BinaryParser.EncodeTable(table);
    }

    public static ShaderModulesInfo DecodeShaderModulesInfo(ReadOnlySpan<byte> data)
    {
        BinaryTable table = BinaryParser.DecodeTable(data);
        string name = table.GetString(nameof(ShaderModulesInfo.Name));
        string[] defines = Array.Empty<string>();

        if (table.TryGetArray(nameof(ShaderModulesInfo.Defines), out BinaryArray? definesArray))
        {
            defines = new string[definesArray.Count];
            for (int i = 0; i < definesArray.Count; i++)
            {
                if (definesArray.TryGetString(i, out string? define))
                {
                    defines[i] = define;
                }
                else
                {
                    defines[i] = string.Empty;
                }
            }
        }

        ShaderModule? vertexShader = null;
        if (table.TryGetBinary(nameof(ShaderModulesInfo.VertexShader), out ReadOnlyMemory<byte> vertexData))
        {
            vertexShader = DecodeShaderModule(vertexData.Span);
        }

        ShaderModule? fragmentShader = null;
        if (table.TryGetBinary(nameof(ShaderModulesInfo.FragmentShader), out ReadOnlyMemory<byte> fragmentData))
        {
            fragmentShader = DecodeShaderModule(fragmentData.Span);
        }

        ShaderModule? computeShader = null;
        if (table.TryGetBinary(nameof(ShaderModulesInfo.ComputeShader), out ReadOnlyMemory<byte> computeData))
        {
            computeShader = DecodeShaderModule(computeData.Span);
        }

        // Reconstruct reflection info based on the available shader modules
        ShaderReflectionInfo reflectionInfo;
        if (vertexShader.HasValue && fragmentShader.HasValue)
        {
            reflectionInfo = ShaderRelfectionUtility.GetSpirvReflection(vertexShader.Value.Source, fragmentShader.Value.Source, true);
        }
        else if (computeShader.HasValue)
        {
            reflectionInfo = ShaderRelfectionUtility.GetSpirvReflection(computeShader.Value.Source, true);
        }
        else
        {
            throw new InvalidOperationException("Invalid shader module data: no valid shader modules found.");
        }

        return new ShaderModulesInfo(
            name,
            defines,
            vertexShader,
            fragmentShader,
            computeShader,
            reflectionInfo);
    }

    private static string NoIncludeResolver(string includeName)
    {
        throw new InvalidOperationException($"Include statement found in the shader but no include resolver is provided.");
    }
}