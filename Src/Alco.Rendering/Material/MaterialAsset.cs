namespace Alco.Rendering;

/// <summary>
/// Data-only description of one material — the runtime form of a material asset file
/// (<c>.amat</c>). Pure data: no GPU objects, and texture references stay paths so parsing
/// never blocks on bulk texture IO (textures are resolved separately at warm-up or first
/// use, then bound into the compiled materials via <see cref="MaterialCompiler.BindTextures"/>).
/// Per-pass GPU materials are derived from this description by <see cref="MaterialCompiler"/>
/// from the policies of the registered <see cref="IMaterialPass"/>es.
/// <br/>The asset carries only pipeline-agnostic concepts: which surface module to evaluate,
/// its specialization defines, its texture slots and its parameter values. Pipeline-family
/// data (the PBR factors and alpha routing of World3D's materials, ...) lives on derived
/// classes selected by the file's <c>type</c> discriminator; pass implementations receive
/// the derived type statically through <see cref="IMaterialPass{TAsset}"/>.
/// </summary>
public class MaterialAsset
{
    /// <summary>Format version of the material asset files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The material name; defaults to the source file name when the file omits it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Asset path of the surface module the material evaluates; null selects the default
    /// surface of the compiling <see cref="MaterialCompiler"/>. Pass templates specialize
    /// their generic entry points with the module's public surface implementation.
    /// </summary>
    public string? SurfaceShader { get; init; }

    /// <summary>
    /// Specialization defines of the surface, baked into the compiled shader permutations
    /// (e.g. feature toggles of the surface's own code).
    /// </summary>
    public IReadOnlyList<string> Defines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The texture slots of the material: material slot name → texture path relative to
    /// the asset root. A slot name is the shader resource name without the leading
    /// underscore (<c>albedoTexture</c> binds <c>_albedoTexture</c>, <c>noiseMap</c>
    /// binds <c>_noiseMap</c>); the surface declares the slots it needs. Unknown slot
    /// names are rejected at compile time. Never loaded by the parser.
    /// </summary>
    public IReadOnlyDictionary<string, string> Textures { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Surface parameter values by member name of the surface's
    /// <c>[MaterialParams]</c>-marked blocks (any block names, any number of blocks):
    /// 1-4 float components per value, packed at the member offsets slang reflects.
    /// </summary>
    public IReadOnlyDictionary<string, float[]> Parameters { get; init; } = new Dictionary<string, float[]>();

    /// <summary>
    /// The fallback texture policy of one surface texture slot, consulted when the slot
    /// has no texture or its texture is still streaming. The base policy is always
    /// <see cref="MaterialTextureFallback.White"/>; pipeline-family assets override —
    /// e.g. the World3D PBR asset requests flat normals for <c>normal*</c> slots and
    /// black for <c>emissive*</c> ones.
    /// </summary>
    /// <param name="slot">The material texture slot (the shader resource name without the leading underscore).</param>
    public virtual MaterialTextureFallback GetTextureFallback(string slot) => MaterialTextureFallback.White;

    /// <summary>
    /// Enumerate the texture paths referenced by this material in slot order, skipping
    /// empty slots.
    /// </summary>
    public IEnumerable<string> EnumerateTexturePaths()
    {
        foreach (KeyValuePair<string, string> pair in Textures)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                yield return pair.Value;
            }
        }
    }
}
