using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Data-only description of one material — the runtime form of a material asset
/// file (<c>.amat</c>), deserialized directly from JSON. It carries only
/// pipeline-agnostic concepts: which surface module (<see cref="ShaderLibrary"/>)
/// to evaluate, its texture slots and its parameter values; pipeline-family data
/// lives on derived classes, which the compiling facility receives directly.
/// Per-pass GPU materials are compiled from this description by
/// <see cref="MaterialCompiler"/>.
/// </summary>
public class MaterialAsset : IJsonOnDeserialized
{
    private ShaderLibrary? _surface;
    private IReadOnlyDictionary<string, ShaderValue> _parameters = new Dictionary<string, ShaderValue>();
    private IReadOnlyDictionary<string, ShaderValue> _specializations = new Dictionary<string, ShaderValue>();
    private IReadOnlyDictionary<string, GraphicsBuffer>? _parameterBuffers;
    private ShaderLibrary? _parameterBuffersSurface;

    /// <summary>Format version of the material asset files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The format version the file declares; null when constructed in code.</summary>
    public string? Version { get; set; }

    /// <summary>The material name; the loader defaults it to the source file name when the file omits it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The asset-system path the material loaded from, stamped by the loader after
    /// parsing; null for materials constructed in code. Never serialized into
    /// material files — <see cref="JsonConverterMaterialAsset"/> writes it instead,
    /// so assets embedding a material reference roundtrip the loadable path (the
    /// bare <see cref="Name"/> is a display default and does not resolve).
    /// </summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    /// <summary>
    /// The surface library the material evaluates; null selects the default surface of
    /// the compiling <see cref="MaterialCompiler"/>. Pass templates specialize their
    /// generic entry points with the library's public surface implementation.
    /// Setting it drops the shared parameter buffers (see <see cref="ParameterBuffers"/>).
    /// </summary>
    public ShaderLibrary? Surface
    {
        get => _surface;
        set
        {
            _surface = value;
            _parameterBuffers = null;
        }
    }

    /// <summary>
    /// The texture slots of the material: material slot name → the bound texture. A slot
    /// name is the shader resource name without the leading underscore (<c>albedoTexture</c>
    /// binds <c>_albedoTexture</c>, <c>noiseMap</c> binds <c>_noiseMap</c>); the surface
    /// declares the slots it needs. Unknown slot names are rejected at compile time; slots
    /// the table leaves out bind the asset's fallback policy (<see cref="GetTextureFallback"/>).
    /// </summary>
    public IReadOnlyDictionary<string, Texture2D> Textures { get; set; } =
        new Dictionary<string, Texture2D>();

    /// <summary>
    /// Surface parameter values by member name of the surface's
    /// <c>[MaterialParams]</c>-marked blocks (any block names, any number of blocks).
    /// Values author in their natural shapes — floats, colors, integers, booleans,
    /// arrays — and marshal to each member's reflected type at the offsets the
    /// shader compiler reflects (see <see cref="ShaderValue"/>). Setting the table
    /// drops the shared parameter buffers (see <see cref="ParameterBuffers"/>).
    /// </summary>
    public IReadOnlyDictionary<string, ShaderValue> Parameters
    {
        get => _parameters;
        set
        {
            _parameters = value;
            _parameterBuffers = null;
        }
    }

    /// <summary>
    /// The material's value-specialization table: pass-template generic value
    /// parameters by their declaration names (e.g. <c>isFacade</c>, <c>maxLights</c>),
    /// authored like <see cref="Parameters"/> — booleans as <c>true</c>/<c>false</c>,
    /// integers as numbers. Axes the table leaves out take their type's default
    /// (<see langword="false"/> / <c>0</c>); an unknown axis name fails the
    /// material compile listing the axes the template reflects.
    /// </summary>
    public IReadOnlyDictionary<string, ShaderValue> Specializations
    {
        get => _specializations;
        set => _specializations = value;
    }

    /// <summary>
    /// The packed parameter buffers of the surface's <c>[MaterialParams]</c> blocks —
    /// one shared buffer per block, packed once by the first material compile and
    /// reused by every pass the asset compiles into: the values are the asset's own
    /// and never differ per pass, so per-pass copies would be identical bytes.
    /// Exposed as the base <see cref="GraphicsBuffer"/> on purpose — nothing outside
    /// the packing step can rewrite the shared bytes. Setting <see cref="Surface"/> or
    /// <see cref="Parameters"/> drops the cache; the dropped buffers finalize
    /// themselves once the last material referencing them dies, as the engine's
    /// escapable-binding rule prescribes. Null until the first compile (and when
    /// the surface declares no parameter blocks).
    /// <br/>Compiler-private cache, not asset API: only <see cref="MaterialCompiler"/>
    /// packs and reads it — the asset's public face stays data-only.
    /// </summary>
    internal IReadOnlyDictionary<string, GraphicsBuffer>? ParameterBuffers => _parameterBuffers;

    /// <summary>Whether the shared buffers were packed against this surface (the compiler's default counts too).</summary>
    internal bool HasParameterBuffers(ShaderLibrary surface)
        => _parameterBuffers != null && _parameterBuffersSurface == surface;

    /// <summary>Caches the packed buffers, shared by the passes compiling this asset.</summary>
    internal void SetParameterBuffers(ShaderLibrary surface, IReadOnlyDictionary<string, GraphicsBuffer> buffers)
    {
        _parameterBuffersSurface = surface;
        _parameterBuffers = buffers;
    }

    /// <summary>
    /// The fallback texture policy of one surface texture slot, consulted when the slot
    /// has no texture. The base policy is always
    /// <see cref="MaterialTextureFallback.White"/>; pipeline-family assets override —
    /// e.g. the World3D PBR asset requests flat normals for <c>normal*</c> slots and
    /// black for <c>emissive*</c> ones.
    /// </summary>
    /// <param name="slot">The material texture slot (the shader resource name without the leading underscore).</param>
    public virtual MaterialTextureFallback GetTextureFallback(string slot) => MaterialTextureFallback.White;

    /// <summary>Normalize the deserialized tables (see <see cref="Textures"/>).</summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        Textures = NormalizeSlots(Textures);
        Parameters = NormalizeNames(Parameters);
        Specializations = NormalizeNames(Specializations);
    }

    /// <summary>Texture slots: trimmed slot names; empty slot names and unset (null) entries drop.</summary>
    private static IReadOnlyDictionary<string, Texture2D> NormalizeSlots(
        IReadOnlyDictionary<string, Texture2D> textures)
    {
        Dictionary<string, Texture2D> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Texture2D> pair in textures)
        {
            string slot = pair.Key.Trim();
            if (slot.Length > 0 && pair.Value != null)
            {
                result[slot] = pair.Value;
            }
        }
        return result;
    }

    /// <summary>Value tables (parameters, specializations): trimmed member names; empty names are rejected.</summary>
    private static IReadOnlyDictionary<string, ShaderValue> NormalizeNames(
        IReadOnlyDictionary<string, ShaderValue> values)
    {
        Dictionary<string, ShaderValue> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ShaderValue> pair in values)
        {
            string name = pair.Key.Trim();
            if (name.Length == 0)
            {
                throw new InvalidDataException("GraphicsMaterial asset has an empty parameter name.");
            }
            result[name] = pair.Value;
        }
        return result;
    }
}
