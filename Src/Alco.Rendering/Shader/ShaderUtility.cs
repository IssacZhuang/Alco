using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Matches uses of the DEFINE_TEX2D_DEPTH macro (a depth texture read with Load only).
    /// </summary>
    public static readonly Regex RegexDepthTexture = new Regex(@"\bDEFINE_TEX2D_DEPTH\s*\([^,]+,\s*(\w+)\s*\)", RegexOptions.Compiled);

    /// <summary>
    /// Matches uses of the DEFINE_TEX2D_DEPTH_SAMPLE macro (a depth texture sampled with a comparison sampler).
    /// </summary>
    public static readonly Regex RegexDepthTextureSample = new Regex(@"\bDEFINE_TEX2D_DEPTH_SAMPLE\s*\([^,]+,\s*(\w+)\s*\)", RegexOptions.Compiled);

    /// <summary>
    /// Compiles the shader text with the specified filename and multi-compile defines.
    /// </summary>
    /// <param name="shaderText">The shader text to compile.</param>
    /// <param name="filename">The filename of the shader text.</param>
    /// <param name="multiCompileDefines">The multi-compile defines to use for the shader.</param>
    /// <param name="maxBindGroups">The maximum number of bind groups allowed by the device; the reflection is validated against this limit.</param>
    /// <param name="includeResolver">The function to resolve the include statements.</param>
    /// <returns>The compiled shader result.</returns>
    public static ShaderModulesInfo CompileHLSL(string shaderText, string filename, ReadOnlySpan<string> defines, int maxBindGroups, FileIncludeHandler? includeResolver = null)
    {
        List<ShaderMacroDefine> macros = new List<ShaderMacroDefine>();

        for (int i = 0; i < defines.Length; i++)
        {
            macros.Add(new ShaderMacroDefine(defines[i], DefineTrue));
        }

        string[] defineArray = defines.ToArray();

        // DXC cannot declare depth textures in SPIR-V (the OpTypeImage Depth operand is
        // always "unknown"), so shaders mark them via DEFINE_TEX2D_DEPTH* macros; the
        // compiled modules are rewritten accordingly (see SpirvDepthTexturePatcher).
        string[] depthTextureNames = GetDepthTextureNames(shaderText, out List<string> comparisonSamplerNames);

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
                includeResolver,
                depthTextureNames
                );

            ShaderModule pixel = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Fragment,
                functionPixel!.Name,
                filename,
                macros.ToArray(),
                includeResolver,
                depthTextureNames
                );

            ShaderReflectionInfo reflectionInfo = ShaderReflectionUtility.GetSpirvReflection(vertex.Source, pixel.Source, true);
            MarkDepthComparisonSamplers(reflectionInfo, comparisonSamplerNames);
            ValidateReflection(reflectionInfo, filename, maxBindGroups);
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
                macros.ToArray(),
                includeResolver,
                depthTextureNames
                );

            ShaderReflectionInfo reflectionInfo = ShaderReflectionUtility.GetSpirvReflection(compute.Source, true);
            MarkDepthComparisonSamplers(reflectionInfo, comparisonSamplerNames);
            ValidateReflection(reflectionInfo, filename, maxBindGroups);
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
            reflectionInfo = ShaderReflectionUtility.GetSpirvReflection(vertexShader.Value.Source, fragmentShader.Value.Source, true);
        }
        else if (computeShader.HasValue)
        {
            reflectionInfo = ShaderReflectionUtility.GetSpirvReflection(computeShader.Value.Source, true);
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

    /// <summary>
    /// Collects the depth texture variable names declared via the DEFINE_TEX2D_DEPTH and
    /// DEFINE_TEX2D_DEPTH_SAMPLE macros in the shader text.
    /// </summary>
    /// <param name="shaderText">The shader text to scan.</param>
    /// <param name="comparisonSamplerNames">The sampler names paired with DEFINE_TEX2D_DEPTH_SAMPLE textures (texture name + "Sampler").</param>
    /// <returns>The depth texture variable names.</returns>
    private static string[] GetDepthTextureNames(string shaderText, out List<string> comparisonSamplerNames)
    {
        List<string> names = new List<string>();
        foreach (Match match in RegexDepthTexture.Matches(shaderText))
        {
            names.Add(match.Groups[1].Value);
        }

        comparisonSamplerNames = new List<string>();
        foreach (Match match in RegexDepthTextureSample.Matches(shaderText))
        {
            string name = match.Groups[1].Value;
            names.Add(name);
            comparisonSamplerNames.Add(name + "Sampler");
        }

        return names.ToArray();
    }

    /// <summary>
    /// Re-applies the comparison-sampler markers to shader modules decoded from a
    /// cache. SPIR-V carries no marker for comparison samplers, so the re-reflection
    /// in <see cref="DecodeShaderModulesInfo"/> loses them; the markers are recovered
    /// from the shader text (DEFINE_TEX2D_DEPTH_SAMPLE declarations).
    /// </summary>
    /// <param name="modulesInfo">The decoded shader modules whose reflection info is patched in place.</param>
    /// <param name="shaderText">The original shader text.</param>
    public static void MarkDepthComparisonSamplers(ShaderModulesInfo modulesInfo, string shaderText)
    {
        GetDepthTextureNames(shaderText, out List<string> comparisonSamplerNames);
        MarkDepthComparisonSamplers(modulesInfo.ReflectionInfo, comparisonSamplerNames);
    }

    /// <summary>
    /// Marks the sampler bindings paired with DEFINE_TEX2D_DEPTH_SAMPLE textures as
    /// comparison samplers in the reflection info. SPIR-V carries no marker for
    /// comparison samplers, so reflection cannot detect them on its own.
    /// </summary>
    /// <param name="reflectionInfo">The reflection info to patch in place.</param>
    /// <param name="comparisonSamplerNames">The names of the comparison samplers.</param>
    private static void MarkDepthComparisonSamplers(ShaderReflectionInfo reflectionInfo, List<string> comparisonSamplerNames)
    {
        if (comparisonSamplerNames.Count == 0)
        {
            return;
        }

        foreach (BindGroupLayout layout in reflectionInfo.BindGroups)
        {
            if (layout.Bindings is not BindGroupEntryInfo[] bindings)
            {
                continue;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                ref BindGroupEntryInfo info = ref bindings[i];
                if (info.Entry.Type != BindingType.Sampler)
                {
                    continue;
                }

                if (!comparisonSamplerNames.Contains(info.Entry.Name))
                {
                    continue;
                }

                info.Entry = new BindGroupEntry(
                    info.Entry.Binding,
                    info.Entry.Stage,
                    BindingType.SamplerComparison,
                    name: info.Entry.Name);
            }
        }
    }

    /// <summary>
    /// Validates the bind group layout of the reflection against the device limit, surfacing
    /// violations as <see cref="ShaderValidationException"/> to keep the compile API uniform.
    /// </summary>
    private static void ValidateReflection(ShaderReflectionInfo reflectionInfo, string filename, int maxBindGroups)
    {
        try
        {
            ShaderReflectionUtility.ValidateBindGroupLayouts(reflectionInfo, maxBindGroups, filename);
        }
        catch (ShaderReflectionException e)
        {
            throw new ShaderValidationException(e.Message);
        }
    }

    private static string NoIncludeResolver(string includeName)
    {
        throw new InvalidOperationException($"Include statement found in the shader but no include resolver is provided.");
    }
}