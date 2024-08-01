using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Graphics;
using Vocore.ShaderCompiler;

namespace Vocore.Rendering;

public static class UtilsShaderHLSL
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
    public const int MaxRecursionDepth = 32;

    public static ShaderCompileResult Compile(string shaderText, string filename, Func<string, string>? includeResolver = null)
    {
        ShaderPreproccessResultHLSL preproccessed = PreprocessText(shaderText, filename, includeResolver);
        return Compile(preproccessed);
    }

    public static ShaderCompileResult Compile(ShaderPreproccessResultHLSL preproccessed)
    {
        ValidatePreprocessResult(preproccessed);
        if (preproccessed.Stages.IsGraphicsShader())
        {
            ShaderModule vertex = ShaderCompilerDxc.CrearteSpirvShaderModule(preproccessed.ShaderText, ShaderStage.Vertex, preproccessed.EntryVertex!, preproccessed.Filename);
            ShaderModule fragment = ShaderCompilerDxc.CrearteSpirvShaderModule(preproccessed.ShaderText, ShaderStage.Fragment, preproccessed.EntryFragment!, preproccessed.Filename);
            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(vertex.Source, fragment.Source, true);
            return ShaderCompileResult.CreateGraphics(vertex, fragment, preproccessed, reflectionInfo);
        }
        else if (preproccessed.Stages.IsComputeShader())
        {
            ShaderModule compute = ShaderCompilerDxc.CrearteSpirvShaderModule(preproccessed.ShaderText, ShaderStage.Compute, preproccessed.EntryCompute!, preproccessed.Filename);
            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(compute.Source, true);
            return ShaderCompileResult.CreateCompute(compute, preproccessed, reflectionInfo);
        }
        else
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }
    }

    public static ShaderPreproccessResultHLSL PreprocessText(string shaderText, string filename, Func<string, string>? includeResolver = null)
    {
        return PreprocessText(shaderText, filename, includeResolver, 0);
    }


    private static ShaderPreproccessResultHLSL PreprocessText(string shaderText, string filename, Func<string, string>? includeResolver, int depth)
    {
        if (includeResolver == null)
        {
            includeResolver = NoIncludeResolver;
        }

        ShaderPreproccessResultHLSL result = new ShaderPreproccessResultHLSL();
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

                if (UtilsShaderText.TryGetPragma(line, out ShaderPragma pragma))
                {
                    shaderPragmas.Add(pragma);

                    if (UtilsShaderText.TryGetBlendState(pragma, out BlendState blendState))
                    {
                        result.BlendState = blendState;
                    }

                    if (UtilsShaderText.TryGetRasterizerState(pragma, out RasterizerState rasterizerState))
                    {
                        result.RasterizerState = rasterizerState;
                    }

                    if (UtilsShaderText.TryGetDepthStencilState(pragma, out DepthStencilState depthStencilState))
                    {
                        result.DepthStencilState = depthStencilState;
                    }

                    if (UtilsShaderText.TryGetPrimitiveTopology(pragma, out PrimitiveTopology? primitiveTopology))
                    {
                        result.PrimitiveTopology = primitiveTopology;
                    }

                    if (UtilsShaderText.TryGetVertexEntryPoint(pragma, out string? entryPoint))
                    {
                        result.EntryVertex = entryPoint;
                        result.Stages |= ShaderStage.Vertex;
                    }

                    if (UtilsShaderText.TryGetFragmentEntryPoint(pragma, out entryPoint))
                    {
                        result.EntryFragment = entryPoint;
                        result.Stages |= ShaderStage.Fragment;
                    }

                    if (UtilsShaderText.TryGetComputeEntryPoint(pragma, out entryPoint))
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

                    ShaderPreproccessResultHLSL includedResult = PreprocessText(includedText, filename, includeResolver, depth + 1);
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

                    if (includedResult.PrimitiveTopology.HasValue)
                    {
                        result.PrimitiveTopology = includedResult.PrimitiveTopology;
                    }
                }
                else
                {
                    builder.AppendLine(line);
                }
            }

            //default
            if (!result.RasterizerState.HasValue)
            {
                result.RasterizerState = RasterizerState.CullNone;
            }

            if (!result.DepthStencilState.HasValue)
            {
                result.DepthStencilState = DepthStencilState.Default;
            }

            if (!result.BlendState.HasValue)
            {
                result.BlendState = BlendState.Opaque;
            }

            if (!result.PrimitiveTopology.HasValue)
            {
                result.PrimitiveTopology = PrimitiveTopology.TriangleList;
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
    public static void ValidatePreprocessResult(ShaderPreproccessResultHLSL result)
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