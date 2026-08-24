using System.Numerics;

namespace Alco.World3D;

/// <summary>
/// Data-only description of one material — the runtime form of a material asset file
/// (<c>.amat</c>). Pure data: no GPU objects, and texture references stay paths so parsing
/// never blocks on bulk texture IO (textures are resolved separately at warm-up or first
/// use, then bound into the compiled materials via <see cref="MaterialCompiler.BindTextures"/>).
/// Per-pass GPU materials are derived from this description by <see cref="MaterialCompiler"/>
/// from the policies of the registered <see cref="MaterialPassDesc"/>s.
/// <br/>A material naming no <see cref="SurfaceShader"/> uses the built-in PbrStandard
/// surface (glTF metallic-roughness) and reads the flat factor fields plus the four
/// standard texture slots; a material naming a surface shader evaluates that surface
/// instead, with its own texture slots (keys of <see cref="Textures"/>) and specialization
/// <see cref="Defines"/>.
/// </summary>
public sealed class MaterialAsset
{
    /// <summary>Format version of the material asset files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The material name; defaults to the source file name when the file omits it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Asset path of the surface module the material evaluates; null selects the built-in
    /// PbrStandard surface. Pass templates specialize their generic entry points with the
    /// module's public <c>Surface : ISurface</c> implementation.
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
    /// binds <c>_noiseMap</c>); the built-in surface declares the four standard slots
    /// (<c>albedoTexture</c>, <c>normalTexture</c>, <c>metallicRoughnessTexture</c>,
    /// <c>emissiveTexture</c>), custom surfaces the slots they declare. Unknown slot
    /// names are rejected at compile time. Never loaded by the parser.
    /// </summary>
    public IReadOnlyDictionary<string, string> Textures { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Surface parameter values by member name of the surface's
    /// <c>_materialParams</c> block (see the convention in Shaders/Libs/Surface.slang):
    /// 1-4 float components per value, one <c>float4</c> register each. Only custom
    /// surfaces declare such a block; the built-in surface's knobs are the flat factor
    /// fields, which ride the instance buffers instead.
    /// </summary>
    public IReadOnlyDictionary<string, float[]> Parameters { get; init; } = new Dictionary<string, float[]>();

    /// <summary>Linear base color factor, multiplied with the albedo texture.</summary>
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;

    /// <summary>Metallic factor in [0, 1].</summary>
    public float MetallicFactor { get; init; }

    /// <summary>Roughness factor in [0, 1].</summary>
    public float RoughnessFactor { get; init; } = 1.0f;

    /// <summary>Linear emissive color factor, multiplied with the emissive texture.</summary>
    public Vector3 EmissiveFactor { get; init; } = Vector3.Zero;

    /// <summary>The alpha handling mode.</summary>
    public MeshAlphaMode AlphaMode { get; init; }

    /// <summary>Alpha cutoff used when <see cref="AlphaMode"/> is <see cref="MeshAlphaMode.Mask"/>.</summary>
    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>Whether both faces of triangles are rendered.</summary>
    public bool DoubleSided { get; init; }

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
