using System.Collections.Concurrent;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Loads World3D pipeline shaders (the HLSL assets under
/// <c>Assets/Shaders</c>) compiled through the Slang front end instead of the
/// engine's DXC toolchain. The same source text is handed to Slang (it is
/// compatible through the <c>__SLANG__</c> guard in Core.hlsli), includes are
/// resolved through the same asset system, and the resulting
/// <see cref="Shader"/> is built from Slang's own reflection - so renderers
/// and render-graph nodes consume it exactly like an
/// <c>AssetSystem.Load&lt;Shader&gt;</c> result. One <see cref="Shader"/> is
/// created per asset path; the engine's Shader caches further
/// defines-permutations internally.
/// </summary>
public sealed class SlangPipelineShaderFactory : IDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly AssetSystem _assets;
    private readonly SlangShaderCompiler _compiler = new();
    private readonly ConcurrentDictionary<string, Shader> _shaders = new(StringComparer.Ordinal);

    /// <summary>Create a factory bound to a rendering system and asset system.</summary>
    /// <param name="rendering">The rendering system that owns the created shaders.</param>
    /// <param name="assets">The asset system serving shader sources and includes.</param>
    public SlangPipelineShaderFactory(RenderingSystem rendering, AssetSystem assets)
    {
        _rendering = rendering;
        _assets = assets;
    }

    /// <summary>
    /// Try to compile (or fetch the cached) Slang version of a pipeline shader
    /// asset, e.g. <c>World3DAssetPaths.Shader_GBuffer</c>.
    /// </summary>
    /// <param name="assetPath">The shader asset path.</param>
    /// <param name="shader">The Slang-compiled shader on success.</param>
    /// <returns>False when the asset is missing; compile errors throw.</returns>
    public bool TryLoad(string assetPath, out Shader shader)
    {
        if (_shaders.TryGetValue(assetPath, out Shader? cached))
        {
            shader = cached;
            return true;
        }

        if (!_assets.TryGetStream(assetPath, out Stream? stream))
        {
            shader = null!;
            return false;
        }

        string source;
        using (stream)
        {
            using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
            source = reader.ReadToEnd();
        }

        // Flatten includes exactly like the engine's HLSL asset loader
        // (AssetLoaderShaderHLSL): the depth-texture and comparison-sampler
        // conventions are discovered by scanning the source text, so
        // declarations inside included .hlsli files must be visible. Slang
        // then compiles one self-contained translation unit.
        IncludeHelper includeHelper = new();
        source = includeHelper.ProcessInclude(source, assetPath, ResolveFlattenedInclude);

        // The provider is called once per defines permutation by the engine's
        // Shader (on demand); compiles go through the shared session.
        shader = _rendering.CreateShader(
            assetPath,
            defines => _compiler.CompileEngineShader(
                assetPath, source, defines, ResolveInclude));
        _shaders[assetPath] = shader;
        return true;
    }

    /// <summary>Load the Slang version of a pipeline shader; throws when the asset is missing.</summary>
    /// <param name="assetPath">The shader asset path.</param>
    /// <returns>The Slang-compiled shader.</returns>
    public Shader Load(string assetPath)
    {
        if (!TryLoad(assetPath, out Shader shader))
        {
            throw new InvalidDataException($"Pipeline shader '{assetPath}' was not found in the asset system.");
        }
        return shader;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _compiler.Dispose();
        foreach (Shader shader in _shaders.Values)
        {
            shader.Dispose();
        }
        _shaders.Clear();
    }

    /// <summary>
    /// Resolve one <c>#include</c> during flattening: the raw asset path (the
    /// engine's includes are all "Shaders/..." asset-rooted).
    /// </summary>
    private string ResolveFlattenedInclude(string includeName)
    {
        if (TryReadText(includeName, out string? content))
        {
            return content!;
        }
        throw new InvalidDataException($"Shader include '{includeName}' was not found in the asset system.");
    }

    /// <summary>
    /// Resolve a path Slang's preprocessor asks for (the material path and any
    /// not-yet-flattened include). Slang resolves includes against the
    /// including file's directory and asks exactly once (no bare-path
    /// fallback like DXC's include dirs), so serve the longest suffix that
    /// resolves in the asset system.
    /// </summary>
    private string? ResolveInclude(string path)
    {
        // Slang resolves includes against the including file's directory and
        // asks exactly once (no bare-path fallback like DXC's include dirs),
        // so a request for "Shaders/Libs/Core.hlsli" from a translation unit
        // named "Shaders/Pipelines/.../GBuffer.hlsl" arrives as the joined
        // path. Serve the longest suffix that resolves in the asset system -
        // the engine's asset paths are all "Shaders/..." roots.
        if (TryReadText(path, out string? content))
        {
            return content;
        }

        int boundary = path.IndexOf('/', StringComparison.Ordinal);
        while (boundary >= 0 && boundary + 1 < path.Length)
        {
            string suffix = path[(boundary + 1)..];
            if (TryReadText(suffix, out content))
            {
                return content;
            }
            boundary = path.IndexOf('/', boundary + 1);
        }
        return null;
    }

    private bool TryReadText(string path, out string? content)
    {
        if (!_assets.TryGetStream(path, out Stream? stream))
        {
            content = null;
            return false;
        }

        using (stream)
        {
            using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
            content = reader.ReadToEnd();
            return true;
        }
    }
}
