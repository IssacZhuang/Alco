using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Data-only description of a material asset file (<c>.amat</c>) that per-pass GPU materials are compiled from.
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
    /// The asset-system path the material was loaded from; null for materials constructed in code.
    /// </summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    /// <summary>
    /// The surface library the material evaluates; null selects the compiler's default surface.
    /// Setting it drops the shared parameter buffers.
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
    /// The texture slots of the material, keyed by shader resource name without the leading underscore.
    /// </summary>
    public IReadOnlyDictionary<string, Texture2D> Textures { get; set; } =
        new Dictionary<string, Texture2D>();

    /// <summary>
    /// Surface parameter values keyed by the name of the surface's <c>[MaterialParams]</c> members.
    /// Setting the table drops the shared parameter buffers.
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
    /// The material's value-specialization table, keyed by pass-template generic parameter names.
    /// </summary>
    public IReadOnlyDictionary<string, ShaderValue> Specializations
    {
        get => _specializations;
        set => _specializations = value;
    }

    /// <summary>
    /// The packed parameter buffers shared by every pass compiled from this asset; null until the first compile.
    /// Compiler-private cache: only <see cref="MaterialCompiler"/> packs and reads it.
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
    /// The fallback texture policy of one surface texture slot, consulted when the slot has no texture.
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
