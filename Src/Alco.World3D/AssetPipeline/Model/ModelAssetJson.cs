using System.Text.Json;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// JSON schema of a model asset file (<c>.amdl</c>): a mesh reference plus the
/// material-slot bindings. All references are asset-root-relative paths resolved through
/// the asset system at load time.
/// </summary>
internal sealed class ModelAssetJson
{
    public sealed class SlotJson
    {
        public string? Name { get; set; }
        public string? Material { get; set; }
    }

    public string? Version { get; set; }
    public string? Mesh { get; set; }
    public List<SlotJson>? Slots { get; set; }
    public string? DefaultMaterial { get; set; }

    /// <summary>
    /// Parse model asset bytes into the validated schema.
    /// </summary>
    /// <param name="data">The UTF-8 JSON bytes of the file.</param>
    /// <param name="filename">The file being parsed, for error context.</param>
    /// <returns>The parsed schema.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is empty, has an
    /// unsupported version, lacks a mesh reference, or a slot binding is malformed.</exception>
    public static ModelAssetJson Parse(ReadOnlySpan<byte> data, string filename)
    {
        ModelAssetJson? json;
        try
        {
            json = JsonSerializer.Deserialize<ModelAssetJson>(data, AssetJson.Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Model asset '{filename}' is not valid JSON: {exception.Message}", exception);
        }

        if (json == null)
        {
            throw new InvalidDataException($"Model asset '{filename}' is empty.");
        }

        AssetJson.ValidateVersion(json.Version, ModelAsset.FormatVersion, "Model asset", filename);

        if (string.IsNullOrWhiteSpace(json.Mesh))
        {
            throw new InvalidDataException($"Model asset '{filename}' has no mesh reference.");
        }

        if (json.Slots != null)
        {
            for (int i = 0; i < json.Slots.Count; i++)
            {
                SlotJson slot = json.Slots[i];
                if (string.IsNullOrWhiteSpace(slot.Name) || string.IsNullOrWhiteSpace(slot.Material))
                {
                    throw new InvalidDataException($"Model asset '{filename}' slot #{i} must define both 'name' and 'material'.");
                }
            }
        }

        return json;
    }
}
