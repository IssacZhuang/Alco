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
    /// Represents the key for the "#pragma" directive.
    /// </summary>
    public const string KeyPragma = "#pragma";
    /// <summary>
    /// Represents the key for the "#define" directive.
    /// </summary>
    public const string DefineTrue = "1";
    /// <summary>
    /// Represents the key used for including shader code.
    /// </summary>
    public const string KeyInclude = "#include"; // #include "Base.hlsli"
    /// <summary>
    /// Represents the format string for a line directive in a shader file.
    /// </summary>
    public const string FormatLine = "#line {0} \"{1}\""; // #line line filename
    public const int MaxRecursionDepth = 32;


    //deprecated
    public static ShaderCompileResultDeprecated Compile(string shaderText, string filename, Func<string, string>? includeResolver = null)
    {
        ShaderPreproccessResultHLSLDeprecated preproccessed = PreprocessText(shaderText, filename, includeResolver);
        return Compile(preproccessed);
    }

    //deprecated
    public static ShaderCompileResultDeprecated Compile(ShaderPreproccessResultHLSLDeprecated preproccessed)
    {
        ValidatePreprocessResult(preproccessed);
        if (preproccessed.Stages.IsGraphicsShader())
        {
            ShaderModule vertex = ShaderCompilerDxc.CrearteSpirvShaderModule(preproccessed.ShaderText, ShaderStage.Vertex, preproccessed.EntryVertex!, preproccessed.Filename, Span<ShaderMacroDefine>.Empty);
            ShaderModule fragment = ShaderCompilerDxc.CrearteSpirvShaderModule(preproccessed.ShaderText, ShaderStage.Fragment, preproccessed.EntryFragment!, preproccessed.Filename, Span<ShaderMacroDefine>.Empty);
            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(vertex.Source, fragment.Source, true);
            return ShaderCompileResultDeprecated.CreateGraphics(vertex, fragment, preproccessed, reflectionInfo);
        }
        else if (preproccessed.Stages.IsComputeShader())
        {
            ShaderModule compute = ShaderCompilerDxc.CrearteSpirvShaderModule(preproccessed.ShaderText, ShaderStage.Compute, preproccessed.EntryCompute!, preproccessed.Filename, Span<ShaderMacroDefine>.Empty);
            ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(compute.Source, true);
            return ShaderCompileResultDeprecated.CreateCompute(compute, preproccessed, reflectionInfo);
        }
        else
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }
    }

    /// <summary>
    /// Compiles the shader text with the specified filename and multi-compile defines.
    /// </summary>
    /// <param name="shaderText">The shader text to compile.</param>
    /// <param name="filename">The filename of the shader text.</param>
    /// <param name="multiCompileDefines">The multi-compile defines to use for the shader.</param>
    /// <param name="includeResolver">The function to resolve the include statements.</param>
    /// <returns>The compiled shader result.</returns>
    public static ShaderCompileResult Compile(string shaderText, string filename, Span<string> defines, FileIncludeHandler? includeResolver = null)
    {
        List<ShaderVariant> modules = new List<ShaderVariant>();
        List<ShaderMacroDefine> macros = new List<ShaderMacroDefine>();

        for (int i = 0; i < defines.Length; i++)
        {
            macros.Add(new ShaderMacroDefine(defines[i], DefineTrue));
        }

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
            

            ShaderModule vertextOrigin = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Vertex,
                ShaderEntry.Vertex,
                filename,
                macros.ToArray(),
                includeResolver
                );

            ShaderModule fragmentOrigin = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Fragment,
                ShaderEntry.Fragment,
                filename,
                macros.ToArray(),
                includeResolver
                );

            ShaderReflectionInfo reflectionInfoZero = UtilsShaderRelfection.GetSpirvReflection(vertextOrigin.Source, fragmentOrigin.Source, true);
            ShaderVariant variantZero = ShaderVariant.CreateGraphics(Array.Empty<string>(), vertextOrigin, fragmentOrigin, reflectionInfoZero);
            modules.Add(variantZero);

            return new ShaderCompileResult(modules.ToArray());
        }else if(stage.HasFlag(ShaderStage.Compute))
        {
            

            //add shader modules with zero defines
            ShaderModule computeOrigin = ShaderCompilerDxc.CrearteSpirvShaderModule(
                shaderText,
                ShaderStage.Compute,
                ShaderEntry.Compute,
                filename,
                macros.ToArray()
                );

            ShaderReflectionInfo reflectionInfoZero = UtilsShaderRelfection.GetSpirvReflection(computeOrigin.Source, true);
            ShaderVariant variantZero = ShaderVariant.CreateCompute(Array.Empty<string>(), computeOrigin, reflectionInfoZero);
            modules.Add(variantZero);
            
            return new ShaderCompileResult(modules.ToArray());
        }
        else
        {
            throw new ShaderValidationException("No entry point defined in the shader.");
        }
    }

    /// <summary>
    /// Processes the include statements in the shader text.
    /// </summary>
    /// <param name="shaderText">The shader text to process.</param>
    /// <param name="filename">The filename of the shader text.</param>
    /// <param name="includeResolver">The function to resolve the include statements.</param>
    /// <returns>The shader text with the include statements resolved.</returns>
    public static string ProcessInclude(string shaderText, string filename, Func<string, string> includeResolver, int depth = 0)
    {
        if (depth > MaxRecursionDepth)
        {
            throw new InvalidOperationException($"Include recursion depth exceeds the maximum depth of {MaxRecursionDepth}. It might be looping include in the file.");
        }

        StringBuilder builder = new StringBuilder();
        using StringReader reader = new StringReader(shaderText);
        HashSet<string> includedFiles = new HashSet<string>();
        string? line;
        int lineCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineCount++;
            line = line.TrimStart();

            if (TryGetInclude(line, includeResolver, out string? includedText, out string? includeFilename) &&
                !includedFiles.Contains(includeFilename))
            {
                includedFiles.Add(includeFilename);

                includedText = ProcessInclude(includedText, includeFilename, includeResolver, depth + 1);

                builder.AppendLine(string.Format(FormatLine, 1, includeFilename));
                builder.AppendLine(includedText);
                builder.AppendLine(string.Format(FormatLine, lineCount + 1, filename));
            }
            else
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    //deprecated
    public static ShaderPreproccessResultHLSLDeprecated PreprocessText(string shaderText, string filename, Func<string, string>? includeResolver = null)
    {
        return PreprocessText(shaderText, filename, includeResolver, 0);
    }


    //deprecated
    private static ShaderPreproccessResultHLSLDeprecated PreprocessText(string shaderText, string filename, Func<string, string>? includeResolver, int depth)
    {
        if (includeResolver == null)
        {
            includeResolver = NoIncludeResolver;
        }

        ShaderPreproccessResultHLSLDeprecated result = new ShaderPreproccessResultHLSLDeprecated();
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

                    ShaderPreproccessResultHLSLDeprecated includedResult = PreprocessText(includedText, filename, includeResolver, depth + 1);
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
    public static void ValidatePreprocessResult(ShaderPreproccessResultHLSLDeprecated result)
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
                try
                {
                    includedText = includeResolver(includeName);
                    includedFilename = includeName;
                    return true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error resolving include statement: {shaderTextLine}", ex);
                }
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