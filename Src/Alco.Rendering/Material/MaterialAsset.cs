using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Data-only description of an authored material asset (<c>.amat</c>) holding surface module selection,
/// texture slot bindings, uniform parameters, and value specialization arguments.
/// </summary>
public class MaterialAsset : IJsonOnDeserialized
{
    /// <summary>Format version of material asset files consumed by this runtime.</summary>
    public const string FormatVersion = "1.0";

    private string _name = string.Empty;
    private IReadOnlyDictionary<string, Image> _textures = new Dictionary<string, Image>();
    private IReadOnlyDictionary<string, ShaderValue> _parameters = new Dictionary<string, ShaderValue>();
    private IReadOnlyDictionary<string, ShaderValue> _specializations = new Dictionary<string, ShaderValue>();

    /// <summary>Gets or sets the format version declared in the file; null when constructed in code.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the material name; defaulted from the source file name when omitted.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Gets or sets the asset path the material was loaded from; null when constructed in code.</summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    /// <summary>Gets or sets the surface shader module name; null selects the caller's default surface.</summary>
    public string? Surface { get; set; }

    /// <summary>Gets or sets authored texture bindings keyed by shader parameter name inside the material block.</summary>
    public IReadOnlyDictionary<string, Image> Textures
    {
        get => _textures;
        set => _textures = value ?? new Dictionary<string, Image>();
    }

    /// <summary>Gets or sets authored parameter values keyed by shader member name inside the material block.</summary>
    public IReadOnlyDictionary<string, ShaderValue> Parameters
    {
        get => _parameters;
        set => _parameters = value ?? new Dictionary<string, ShaderValue>();
    }

    /// <summary>Gets or sets authored value-specialization arguments keyed by shader generic parameter name.</summary>
    public IReadOnlyDictionary<string, ShaderValue> Specializations
    {
        get => _specializations;
        set => _specializations = value ?? new Dictionary<string, ShaderValue>();
    }

    /// <summary>
    /// Creates a <see cref="Material"/> from <paramref name="shader"/> specialized with this asset's
    /// <see cref="Specializations"/>, then binds its textures and uniform parameters.
    /// </summary>
    /// <param name="shader">The shader defining the variant and root layout.</param>
    /// <param name="name">Optional runtime material name; defaults to the shader name.</param>
    /// <param name="parameterPrefix">Root field prefix prepended to parameter and texture names (default <c>"Material."</c>).</param>
    /// <returns>The created and populated runtime material.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a specialization, texture slot, or parameter mismatches the shader layout.</exception>
    public Material CreateMaterial(Shader shader, string name = "", string parameterPrefix = "Material.")
    {
        ArgumentNullException.ThrowIfNull(shader);

        IReadOnlyList<string> valueParamSlots = shader.ValueSpecializationParameters;
        object[] ordered = valueParamSlots.Count > 0 || Specializations.Count > 0
            ? ResolveSpecializations(shader.Name, valueParamSlots)
            : [];
        Material material = ordered.Length > 0
            ? shader.CreateMaterial(name, ordered)
            : shader.CreateMaterial(name);
        ApplyBindingsTo(material, parameterPrefix);
        return material;
    }

    /// <summary>Applies this asset's specializations, textures, and uniform parameters onto <paramref name="material"/>.</summary>
    /// <param name="material">The target runtime material.</param>
    /// <param name="parameterPrefix">Root field prefix prepended to parameter and texture names (default <c>"Material."</c>).</param>
    /// <exception cref="InvalidOperationException">Thrown when a specialization, texture slot, or parameter mismatches the shader layout.</exception>
    public void ApplyTo(Material material, string parameterPrefix = "Material.")
    {
        ArgumentNullException.ThrowIfNull(material);

        if (Specializations.Count > 0)
        {
            List<string> valueParamSlots = [];
            foreach (ShaderEntryPointReflection entryPoint in material.Reflection.EntryPoints)
            {
                foreach (ShaderGenericParam gp in entryPoint.GenericParameters)
                {
                    if (!gp.IsTypeParameter)
                        valueParamSlots.Add(gp.Name);
                }
            }

            object[] ordered = ResolveSpecializations(material.Shader.Name, valueParamSlots);
            material.SetSpecializations(ordered);
        }

        ApplyBindingsTo(material, parameterPrefix);
    }

    private object[] ResolveSpecializations(string shaderName, IReadOnlyList<string> valueParamSlots)
    {
        HashSet<string> declaredNames = new(valueParamSlots, StringComparer.Ordinal);
        foreach (string key in Specializations.Keys)
        {
            if (!declaredNames.Contains(key))
                throw new InvalidOperationException(
                    $"Specialization '{key}' in material '{Name}' does not exist on shader '{shaderName}'.");
        }

        if (valueParamSlots.Count == 0)
            return [];

        object[] ordered = new object[valueParamSlots.Count];
        for (int i = 0; i < valueParamSlots.Count; i++)
        {
            string paramName = valueParamSlots[i];
            if (!Specializations.TryGetValue(paramName, out ShaderValue value))
                throw new InvalidOperationException(
                    $"Shader '{shaderName}' requires value specialization '{paramName}' missing in material '{Name}'.");
            ordered[i] = value.ToSpecializationString();
        }
        return ordered;
    }

    private void ApplyBindingsTo(Material material, string parameterPrefix)
    {
        ShaderReflection reflection = material.Reflection;
        string prefix = parameterPrefix ?? string.Empty;

        foreach ((string slot, Image image) in Textures)
        {
            string fieldName = string.Concat(prefix, slot);
            ShaderFieldLayout? field = reflection.Root?.Find(fieldName);
            if (field == null || field.Semantic != ShaderFieldSemantic.TextureId)
                throw new InvalidOperationException(
                    $"Texture slot '{fieldName}' does not exist as a texture parameter on material '{material.Name}' (asset '{Name}').");
            material.SetTexture(field.Name, image.Texture);
        }

        foreach ((string paramName, ShaderValue value) in Parameters)
        {
            string fieldName = string.Concat(prefix, paramName);
            ShaderFieldLayout? field = reflection.Root?.Find(fieldName);
            if (field == null || field.Semantic != ShaderFieldSemantic.Data)
                throw new InvalidOperationException(
                    $"Parameter '{fieldName}' does not exist as a data field on material '{material.Name}' (asset '{Name}').");

            ApplyParameter(material, field, value, fieldName);
        }
    }

    private void ApplyParameter(Material material, ShaderFieldLayout field, ShaderValue value, string fieldName)
    {
        int byteSize = (int)field.SizeBytes;
        byte[]? rented = byteSize > 256 ? new byte[byteSize] : null;
        Span<byte> bytes = rented ?? stackalloc byte[byteSize];
        if (!value.TryWriteTo(field.Type, bytes, out string? error))
            throw new InvalidOperationException(
                $"Parameter '{fieldName}' in material '{Name}' could not be written: {error}");
        material.SetRawData(field.Name, bytes);
    }

    /// <summary>Normalizes deserialized module names, texture slots, parameter names, and specialization keys.</summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        Surface = string.IsNullOrWhiteSpace(Surface) ? null : Surface.Trim();
        Textures = NormalizeSlots(Textures);
        Parameters = NormalizeNames(Parameters);
        Specializations = NormalizeNames(Specializations);
    }

    private static IReadOnlyDictionary<string, Image> NormalizeSlots(IReadOnlyDictionary<string, Image> textures)
    {
        Dictionary<string, Image> result = new(StringComparer.Ordinal);
        foreach ((string key, Image? value) in textures)
        {
            string slot = key.Trim();
            if (slot.Length > 0 && value != null)
                result[slot] = value;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, ShaderValue> NormalizeNames(IReadOnlyDictionary<string, ShaderValue> values)
    {
        Dictionary<string, ShaderValue> result = new(StringComparer.Ordinal);
        foreach ((string key, ShaderValue value) in values)
        {
            string name = key.Trim();
            if (name.Length == 0)
                throw new InvalidDataException("Material asset contains an empty parameter or specialization name.");
            result[name] = value;
        }
        return result;
    }
}
