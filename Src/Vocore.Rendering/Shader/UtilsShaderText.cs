using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Vocore.Graphics;

namespace Vocore.Rendering;

public static partial class UtilsShaderText
{
    /// <summary>
    /// Represents the key for the "#pragma" directive.
    /// </summary>
    public const string KeyPragma = "#pragma";
    public const string PragmaKeyBlendState = "BlendState";
    public const string PragmaKeyDepthStencilState = "DepthStencilState";
    public const string PragmaKeyRasterizerState = "RasterizerState";
    public const string PragmaKeyEntryVertex = "EntryVertex";
    public const string PragmaKeyEntryFragment = "EntryFragment";
    public const string PragmaKeyEntryCompute = "EntryCompute";
    public const string PragmaKeyPrimitiveTopology = "PrimitiveTopology";
    

    [GeneratedRegex(@"\/\/.*|\/\*[\s\S]*?\*\/")]
    private static partial Regex MyRegex();

    public static string RemoveComments(string shaderText)
    {
        return MyRegex().Replace(shaderText, string.Empty);
    }

    public static ShaderPragma[] GetPragmas(string shaderText)
    {
        shaderText = shaderText.Replace("\r\n", "\n");
        shaderText = RemoveComments(shaderText);
        string[] lines = shaderText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        List<ShaderPragma> pragmas = new();
        foreach (string line in lines)
        {
            if (TryGetPragma(line, out ShaderPragma pragma))
            {
                pragmas.Add(pragma);
            }
        }
        return pragmas.ToArray();
    }

    public static bool TryGetPragma(string shaderTextLine, out ShaderPragma pragma)
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



    public static bool TryGetDepthStencilState(ShaderPragma pragma, out DepthStencilState depthStencilState)
    {
        if (pragma.Name != PragmaKeyDepthStencilState)
        {
            depthStencilState = default;
            return false;
        }

        // built-in depth stencil states
        if (pragma.Values.Length == 1)
        {
            string pargmaValue = pragma.Values[0];
            switch (pargmaValue)
            {
                case "Default":
                    depthStencilState = DepthStencilState.Default;
                    return true;
                case "Read":
                    depthStencilState = DepthStencilState.Read;
                    return true;
                case "Write":
                    depthStencilState = DepthStencilState.Write;
                    return true;
                case "None":
                    depthStencilState = DepthStencilState.None;
                    return true;
                default:
                    throw new NotSupportedException($"Depth stencil state {pargmaValue} is not built-in depth stencil state, required 1(built-in) or 2(custom).");
            }
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

    public static bool TryGetRasterizerState(ShaderPragma pragma, out RasterizerState rasterizerState)
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

    public static bool TryGetBlendState(ShaderPragma pragma, out BlendState blendState)
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

    public static bool TryGetPrimitiveTopology(ShaderPragma pragma, [NotNullWhen(true)] out PrimitiveTopology? topology)
    {
        if (pragma.Name != PragmaKeyPrimitiveTopology)
        {
            topology = null;
            return false;
        }

        if (pragma.Values.Length != 1)
        {
            throw new InvalidOperationException($"Invalid primitive topology pragma value count: {pragma.Values.Length}, required 1.");
        }

        topology = (PrimitiveTopology)Enum.Parse(typeof(PrimitiveTopology), pragma.Values[0]);
        return true;
    }


}