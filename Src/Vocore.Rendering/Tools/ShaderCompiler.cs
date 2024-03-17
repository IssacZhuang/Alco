using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Graphics;

namespace Vocore.Rendering;

public static class ShaderCompiler
{
    /// <summary>
    /// Represents the key for the "#pragma" directive.
    /// </summary>
    public const string KeyPragma = "#pragma";
    /// <summary>
    /// Represents the key used for including shader code.
    /// </summary>
    public const string KeyInclude = "#include"; // #include "Base.hlsli"
    /// <summary>
    /// Represents the format string for a line directive in a shader file.
    /// </summary>
    public const string FormatLine = "#line {0} \"{1}\""; // #line line filename
    public const string PragmaKeyBlendState = "BlendState";
    public const string PragmaKeyDepthStencilState = "DepthStencilState";
    public const string PragmaKeyRasterizerState = "RasterizerState";
    public const string PragmaKeyEntryVertex = "EntryVertex";
    public const string PragmaKeyEntryFragment = "EntryFragment";
    public const string PragmaKeyEntryCompute = "EntryCompute";
    public const int MaxRecursionDepth = 32;

    public static readonly byte[] SpirvHeader = new byte[] { 0x03, 0x02, 0x23, 0x07 };

    public static ShaderCompileResult Compile(string shaderText, string filename, Func<string, string>? includeResolver = null)
    {
        ShaderPreproccessResult preproccessed = PreprocessText(shaderText, filename, includeResolver);
        return Compile(preproccessed);
    }

    public static ShaderCompileResult Compile(ShaderPreproccessResult preproccessed)
    {
        ValidatePreprocessResult(preproccessed);
        if (preproccessed.Stages.IsGraphicsShader())
        {
            ShaderStageSource vertex = ShaderCompilerDxc.CrearteSpirvShaderSource(preproccessed.ShaderText, ShaderStage.Vertex, preproccessed.EntryVertex!, preproccessed.Filename);
            ShaderStageSource fragment = ShaderCompilerDxc.CrearteSpirvShaderSource(preproccessed.ShaderText, ShaderStage.Fragment, preproccessed.EntryFragment!, preproccessed.Filename);
            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(vertex.Source, fragment.Source, true);
            return ShaderCompileResult.CreateGraphics(vertex, fragment, preproccessed, reflectionInfo);
        }
        else if (preproccessed.Stages.IsComputeShader())
        {
            ShaderStageSource compute = ShaderCompilerDxc.CrearteSpirvShaderSource(preproccessed.ShaderText, ShaderStage.Compute, preproccessed.EntryCompute!, preproccessed.Filename);
            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(compute.Source, true);
            return ShaderCompileResult.CreateCompute(compute, preproccessed, reflectionInfo);
        }
        else
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }
    }

    public static ShaderPreproccessResult PreprocessText(string shaderText, string filename, Func<string, string>? includeResolver = null)
    {
        return PreprocessText(shaderText, filename, includeResolver, 0);
    }

    public static bool HasSpirvHeader(byte[] data)
    {
        if (data.Length < SpirvHeader.Length)
        {
            return false;
        }

        for (int i = 0; i < SpirvHeader.Length; i++)
        {
            if (data[i] != SpirvHeader[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ShaderPreproccessResult PreprocessText(string shaderText, string filename, Func<string, string>? includeResolver, int depth)
    {
        if (includeResolver == null)
        {
            includeResolver = NoIncludeResolver;
        }

        ShaderPreproccessResult result = new ShaderPreproccessResult();
        result.Filename = filename;
        List<ShaderPragma> shaderPragmas = new List<ShaderPragma>();
        using StringReader reader = new StringReader(shaderText);
        StringBuilder builder = new StringBuilder();
        string? line;
        bool isInCommentBlock = false;
        int lineCount = 0;

        try
        {

            while ((line = reader.ReadLine()) != null)
            {
                lineCount++;
                //remove spaces in the front
                line = line.TrimStart();
                // skip comments
                if (line.StartsWith("//"))
                {
                    continue;
                }

                if (line.Contains("/*"))
                {
                    isInCommentBlock = true;
                }

                if (line.Contains("*/"))
                {
                    isInCommentBlock = false;
                }

                if (isInCommentBlock)
                {
                    continue;
                }

                if (TryGetPragma(line, out ShaderPragma pragma))
                {
                    shaderPragmas.Add(pragma);

                    if (TryGetBlendState(pragma, out BlendState blendState))
                    {
                        result.BlendState = blendState;
                    }

                    if (TryGetRasterizerState(pragma, out RasterizerState rasterizerState))
                    {
                        result.RasterizerState = rasterizerState;
                    }

                    if (TryGetDepthStencilState(pragma, out DepthStencilState depthStencilState))
                    {
                        result.DepthStencilState = depthStencilState;
                    }

                    if (TryGetVertexEntryPoint(pragma, out string? entryPoint))
                    {
                        result.EntryVertex = entryPoint;
                        result.Stages |= ShaderStage.Vertex;
                    }

                    if (TryGetFragmentEntryPoint(pragma, out entryPoint))
                    {
                        result.EntryFragment = entryPoint;
                        result.Stages |= ShaderStage.Fragment;
                    }

                    if (TryGetComputeEntryPoint(pragma, out entryPoint))
                    {
                        result.EntryCompute = entryPoint;
                        result.Stages |= ShaderStage.Compute;
                    }
                }

                if (TryGetInclude(line, includeResolver, out string? includedText, out string? includeFilename))
                {
                    if (depth > MaxRecursionDepth)
                    {
                        throw new InvalidOperationException($"Include recursion depth exceeds the maximum depth of {MaxRecursionDepth}. It might be looping include in the file.");
                    }

                    ShaderPreproccessResult includedResult = PreprocessText(includedText, filename, includeResolver, depth + 1);
                    shaderPragmas.AddRange(includedResult.Pragmas);
                    builder.AppendLine(string.Format(FormatLine, 1, includeFilename));
                    builder.AppendLine(includedResult.ShaderText);
                    builder.AppendLine(string.Format(FormatLine, lineCount+1, filename));

                    if (includedResult.EntryFragment != null && result.EntryFragment == null)
                    {
                        throw new InvalidOperationException($"Multiple entry fragment points are not allowed. 1: {filename}, 2: {includeFilename}");
                    }

                    if (includedResult.EntryVertex != null && result.EntryVertex == null)
                    {
                        throw new InvalidOperationException($"Multiple entry vertex points are not allowed. 1: {filename}, 2: {includeFilename}");
                    }

                    if (includedResult.EntryCompute != null && result.EntryCompute == null)
                    {
                        throw new InvalidOperationException($"Multiple entry compute points are not allowed. 1: {filename}, 2: {includeFilename}");
                    }

                    if (!string.IsNullOrEmpty(includedResult.EntryFragment)&& includedResult.Stages.HasFlag(ShaderStage.Vertex))
                    {
                        result.EntryFragment = includedResult.EntryFragment;
                        result.Stages |= ShaderStage.Fragment;
                    }

                    if (!string.IsNullOrEmpty(includedResult.EntryVertex) && includedResult.Stages.HasFlag(ShaderStage.Vertex))
                    {
                        result.EntryVertex = includedResult.EntryVertex;
                        result.Stages |= ShaderStage.Vertex;
                    }

                    if (!string.IsNullOrEmpty(includedResult.EntryCompute) && includedResult.Stages.HasFlag(ShaderStage.Compute))
                    {
                        result.EntryCompute = includedResult.EntryCompute;
                        result.Stages |= ShaderStage.Compute;
                    }

                    //override the states
                    if (includedResult.BlendState.HasValue)
                    {
                        result.BlendState = includedResult.BlendState;
                    }

                    if (includedResult.DepthStencilState.HasValue)
                    {
                        result.DepthStencilState = includedResult.DepthStencilState;
                    }

                    if (includedResult.RasterizerState.HasValue)
                    {
                        result.RasterizerState = includedResult.RasterizerState;
                    }
                }
                else
                {
                    builder.AppendLine(line);
                }
            }

            //default
            if (result.EntryVertex != null && !result.RasterizerState.HasValue)
            {
                result.RasterizerState = RasterizerState.CullNone;
            }

            if (result.EntryFragment != null && !result.DepthStencilState.HasValue)
            {
                result.DepthStencilState = DepthStencilState.Default;
            }

            if (result.EntryFragment != null && !result.BlendState.HasValue)
            {
                result.BlendState = BlendState.Opaque;
            }

            result.ShaderText = builder.ToString();
            result.Pragmas = shaderPragmas.ToArray();
            return result;
        }
        catch (Exception e)
        {
            throw new ShaderLineException(filename, shaderText, lineCount, e);
        }
    }

    /// <summary>
    /// Validates the preprocess result of a shader.
    /// </summary>
    /// <param name="result">The shader preprocess result to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when the shader preprocess result is invalid.</exception>
    public static void ValidatePreprocessResult(ShaderPreproccessResult result)
    {
        if (string.IsNullOrEmpty(result.EntryVertex) && string.IsNullOrEmpty(result.EntryFragment) && string.IsNullOrEmpty(result.EntryCompute))
        {
            throw new ShaderValidationException("No entry point found in the shader.");
        }

        if (string.IsNullOrEmpty(result.EntryFragment) && !string.IsNullOrEmpty(result.EntryVertex))
        {
            throw new ShaderValidationException("Missing entry fragment point in the shader.");
        }

        if (string.IsNullOrEmpty(result.EntryVertex) && !string.IsNullOrEmpty(result.EntryFragment))
        {
            throw new ShaderValidationException("Missing entry vertex point in the shader.");
        }

        if(!string.IsNullOrEmpty(result.EntryCompute) && (!string.IsNullOrEmpty(result.EntryVertex) || !string.IsNullOrEmpty(result.EntryFragment)))
        {
            throw new ShaderValidationException("No vertex or fragment entry point is allowed in the compute shader.");
        }
    }

    private static bool TryGetPragma(string shaderTextLine, out ShaderPragma pragma)
    {
        if (shaderTextLine.StartsWith(KeyPragma))
        {
            string[] tokens = shaderTextLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                pragma = new ShaderPragma
                {
                    Name = tokens[1],
                    Values = tokens.Skip(2).ToArray()
                };
                return true;
            }
        }

        pragma = default;
        return false;
    }

    private static bool TryGetDepthStencilState(ShaderPragma pragma, out DepthStencilState depthStencilState)
    {
        if (pragma.Name != PragmaKeyDepthStencilState)
        {
            depthStencilState = default;
            return false;
        }

        // built-in depth stencil states
        if (pragma.Values.Length == 1)
        {

        }

        if (pragma.Values.Length == 2)
        {
            depthStencilState = new DepthStencilState(
                bool.Parse(pragma.Values[1]),
                (CompareFunction)Enum.Parse(typeof(CompareFunction), pragma.Values[0])
            );
            return true;
        }

        //TODO: add example for built-in and custom depth stencil state
        throw new NotSupportedException($"Invalid depth stencil state pragma value count: {pragma.Values.Length}, required 1(built-in) or 2(custom).");
    }

    private static bool TryGetRasterizerState(ShaderPragma pragma, out RasterizerState rasterizerState)
    {
        if (pragma.Name != PragmaKeyRasterizerState)
        {
            rasterizerState = default;
            return false;
        }

        // built-in rasterizer states
        if (pragma.Values.Length == 1)
        {
            string pargmaValue = pragma.Values[0];
            switch (pargmaValue)
            {
                case "CullNone":
                    rasterizerState = RasterizerState.CullNone;
                    return true;
                case "CullFront":
                    rasterizerState = RasterizerState.CullFront;
                    return true;
                case "CullBack":
                    rasterizerState = RasterizerState.CullBack;
                    return true;
                case "Wireframe":
                    rasterizerState = RasterizerState.Wireframe;
                    return true;
                default:
                    throw new NotSupportedException($"Rasterizer state {pargmaValue} is not built-in rasterizer state, required 1(built-in) or 3(custom).");
            }
        }

        if (pragma.Values.Length == 3)
        {
            rasterizerState = new RasterizerState(
                (FillMode)Enum.Parse(typeof(FillMode), pragma.Values[0]),
                (CullMode)Enum.Parse(typeof(CullMode), pragma.Values[1]),
                (FrontFace)Enum.Parse(typeof(FrontFace), pragma.Values[2])
            );
            return true;
        }



        throw new NotSupportedException($"Invalid rasterizer state pragma value count: {pragma.Values.Length}.");
    }

    private static bool TryGetBlendState(ShaderPragma pragma, out BlendState blendState)
    {
        if (pragma.Name != PragmaKeyBlendState)
        {
            blendState = default;
            return false;
        }

        // built-in blend states
        if (pragma.Values.Length == 1)
        {
            string pargmaValue = pragma.Values[0];
            switch (pargmaValue)
            {
                case "Opaque":
                    blendState = BlendState.Opaque;
                    return true;
                case "AlphaBlend":
                    blendState = BlendState.AlphaBlend;
                    return true;
                case "Additive":
                    blendState = BlendState.Additive;
                    return true;
                case "PremultipliedAlpha":
                    blendState = BlendState.PremultipliedAlpha;
                    return true;
                case "NonPremultipliedAlpha":
                    blendState = BlendState.NonPremultipliedAlpha;
                    return true;
                default:
                    throw new NotSupportedException($"Blend state {pargmaValue} is not built-in blend state, required 1(built-in) or 6(custom).");
            }
        }

        // custom blend state
        if (pragma.Values.Length == 6)
        {
            BlendComponent color = new BlendComponent(
                (BlendFactor)Enum.Parse(typeof(BlendFactor), pragma.Values[0]),
                (BlendFactor)Enum.Parse(typeof(BlendFactor), pragma.Values[1]),
                (BlendOperation)Enum.Parse(typeof(BlendOperation), pragma.Values[2])
            );

            BlendComponent alpha = new BlendComponent(
                (BlendFactor)Enum.Parse(typeof(BlendFactor), pragma.Values[3]),
                (BlendFactor)Enum.Parse(typeof(BlendFactor), pragma.Values[4]),
                (BlendOperation)Enum.Parse(typeof(BlendOperation), pragma.Values[5])
            );

            blendState = new BlendState(color, alpha);
            return true;
        }

        throw new NotSupportedException($"Invalid blend state pragma value count: {pragma.Values.Length}.");
    }

    public static bool TryGetVertexEntryPoint(ShaderPragma pragma, [NotNullWhen(true)] out string? entryPoint)
    {
        if (pragma.Name != PragmaKeyEntryVertex)
        {
            entryPoint = null;
            return false;
        }

        if (pragma.Values.Length != 1)
        {
            throw new InvalidOperationException($"Invalid vertex entry point pragma value count: {pragma.Values.Length}, required 1.");
        }

        entryPoint = pragma.Values[0];
        return true;
    }

    public static bool TryGetFragmentEntryPoint(ShaderPragma pragma, [NotNullWhen(true)] out string? entryPoint)
    {
        if (pragma.Name != PragmaKeyEntryFragment)
        {
            entryPoint = null;
            return false;
        }

        if (pragma.Values.Length != 1)
        {
            throw new InvalidOperationException($"Invalid fragment entry point pragma value count: {pragma.Values.Length}, required 1.");
        }

        entryPoint = pragma.Values[0];
        return true;
    }

    public static bool TryGetComputeEntryPoint(ShaderPragma pragma, [NotNullWhen(true)] out string? entryPoint)
    {
        if (pragma.Name != PragmaKeyEntryCompute)
        {
            entryPoint = null;
            return false;
        }

        if (pragma.Values.Length != 1)
        {
            throw new InvalidOperationException($"Invalid compute entry point pragma value count: {pragma.Values.Length}, required 1.");
        }

        entryPoint = pragma.Values[0];
        return true;
    }

    private static bool TryGetInclude(string shaderTextLine, Func<string, string> includeResolver, [NotNullWhen(true)] out string? includedText, [NotNullWhen(true)] out string? includedFilename)
    {
        if (shaderTextLine.StartsWith(KeyInclude))
        {
            string[] tokens = shaderTextLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 1)
            {
                string includeName = tokens[1].Trim('\"');
                includedText = includeResolver(includeName);
                includedFilename = includeName;
                return true;
            }
            else
            {
                throw new InvalidOperationException($"Invalid include statement: {shaderTextLine}");
            }
        }

        includedText = null;
        includedFilename = null;
        return false;
    }

    private static string NoIncludeResolver(string includeName)
    {
        throw new InvalidOperationException($"Include statement found in the shader but no include resolver is provided.");
    }
}