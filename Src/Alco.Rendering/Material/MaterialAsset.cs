using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Data-only description of one material — the runtime form of a material asset file
/// (<c>.amat</c>), deserialized directly (no DTO): polymorphism rides the engine's
/// <c>$type</c> discriminator convention (a file without one parses as this base type;
/// pipeline-family assets like World3D's PBR asset are discovered by assembly scan),
/// and resource references land typed — texture slots hold <see cref="Texture2D"/>s
/// resolved by the loader, the surface is a validated <see cref="ShaderLibrary"/>, and
/// parameter values are <see cref="ShaderValue"/>s (authored as numbers, integers,
/// booleans, component objects, colors or arrays). The asset itself touches no asset
/// system and no GPU beyond holding those objects.
/// <br/>The asset carries only pipeline-agnostic concepts: which surface module to
/// evaluate, its specialization defines, its texture slots and its parameter values.
/// Pipeline-family data (the PBR factors and alpha routing of World3D's materials, ...)
/// lives on derived classes; the rendering facility compiling the asset receives
/// the derived type directly (e.g. <c>GetMaterial(PbrMaterialAsset)</c>).
/// <br/>Per-pass GPU materials are compiled from this description by
/// <see cref="MaterialCompiler"/> (each facility's template and factory),
/// binding the carried textures by slot with the
/// asset's fallback policy for unbound slots. The asset is complete at load time and
/// never learns about streaming: streamed texture sources (e.g. a glTF scene's
/// textures) are owned by the streaming consumer and override the compiled materials
/// through <see cref="GraphicsMaterialInstance"/>s.
/// </summary>
public class MaterialAsset : IJsonOnDeserialized
{
    /// <summary>Format version of the material asset files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The format version the file declares; null when constructed in code.</summary>
    public string? Version { get; set; }

    /// <summary>The material name; the loader defaults it to the source file name when the file omits it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The surface library the material evaluates; null selects the default surface of
    /// the compiling <see cref="MaterialCompiler"/>. Pass templates specialize their
    /// generic entry points with the library's public surface implementation.
    /// </summary>
    public ShaderLibrary? Surface { get; set; }

    /// <summary>
    /// Specialization defines of the surface, baked into the compiled shader permutations
    /// (e.g. feature toggles of the surface's own code).
    /// </summary>
    public IReadOnlyList<string> Defines { get; set; } = [];

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
    /// shader compiler reflects (see <see cref="ShaderValue"/>).
    /// </summary>
    public IReadOnlyDictionary<string, ShaderValue> Parameters { get; set; } =
        new Dictionary<string, ShaderValue>();

    /// <summary>
    /// The fallback texture policy of one surface texture slot, consulted when the slot
    /// has no texture. The base policy is always
    /// <see cref="MaterialTextureFallback.White"/>; pipeline-family assets override —
    /// e.g. the World3D PBR asset requests flat normals for <c>normal*</c> slots and
    /// black for <c>emissive*</c> ones.
    /// </summary>
    /// <param name="slot">The material texture slot (the shader resource name without the leading underscore).</param>
    public virtual MaterialTextureFallback GetTextureFallback(string slot) => MaterialTextureFallback.White;

    /// <summary>Normalize the deserialized tables (see <see cref="Defines"/>, <see cref="Textures"/>).</summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        Defines = NormalizeDefines(Defines);
        Textures = NormalizeSlots(Textures);
        Parameters = NormalizeParameters(Parameters);
    }

    /// <summary>Defines: trimmed, empty entries dropped, duplicates removed in first-seen order.</summary>
    private static IReadOnlyList<string> NormalizeDefines(IReadOnlyList<string> defines)
    {
        List<string> result = new(defines.Count);
        foreach (string define in defines)
        {
            string trimmed = define.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (trimmed.Contains(' '))
            {
                throw new InvalidDataException($"GraphicsMaterial asset has a define with whitespace: '{trimmed}'.");
            }
            if (!result.Contains(trimmed))
            {
                result.Add(trimmed);
            }
        }
        return result;
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

    /// <summary>Parameters: trimmed member names; empty names are rejected.</summary>
    private static IReadOnlyDictionary<string, ShaderValue> NormalizeParameters(
        IReadOnlyDictionary<string, ShaderValue> parameters)
    {
        Dictionary<string, ShaderValue> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ShaderValue> pair in parameters)
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
