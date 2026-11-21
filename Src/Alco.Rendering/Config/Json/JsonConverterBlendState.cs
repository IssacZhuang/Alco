using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// JSON converter for <see cref="BlendState"/> preset names.
/// Supports: "Opaque", "AlphaBlend", "Additive", "PremultipliedAlpha", "NonPremultipliedAlpha", "Multiply", "AlphaBlendNoAccumulation".
/// </summary>
public class JsonConverterBlendState : JsonConverter<BlendState>
{
    private static readonly BlendState PresetOpaque = new()
    {
        Enabled = false,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
    };

    private static readonly BlendState PresetAlphaBlend = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    private static readonly BlendState PresetAdditive = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.One, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.One, Operation = BlendOp.Add },
    };

    private static readonly BlendState PresetPremultipliedAlpha = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    private static readonly BlendState PresetNonPremultipliedAlpha = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    private static readonly BlendState PresetMultiply = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.DestinationColor, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
    };

    private static readonly BlendState PresetAlphaBlendNoAccumulation = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.One, Operation = BlendOp.Maximum },
    };

    private static readonly Dictionary<string, BlendState> PresetMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Opaque", PresetOpaque },
        { "AlphaBlend", PresetAlphaBlend },
        { "Additive", PresetAdditive },
        { "PremultipliedAlpha", PresetPremultipliedAlpha },
        { "NonPremultipliedAlpha", PresetNonPremultipliedAlpha },
        { "Multiply", PresetMultiply },
        { "AlphaBlendNoAccumulation", PresetAlphaBlendNoAccumulation }
    };

    private static readonly Dictionary<BlendState, string> ReverseMap = new()
    {
        { PresetOpaque, "Opaque" },
        { PresetAlphaBlend, "AlphaBlend" },
        { PresetAdditive, "Additive" },
        { PresetPremultipliedAlpha, "PremultipliedAlpha" },
        { PresetNonPremultipliedAlpha, "NonPremultipliedAlpha" },
        { PresetMultiply, "Multiply" },
        { PresetAlphaBlendNoAccumulation, "AlphaBlendNoAccumulation" }
    };

    /// <summary>
    /// Reads a JSON string value and converts it to a BlendState preset.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (BlendState).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A BlendState instance from the preset mapping.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or the preset name is not recognized.</exception>
    public override BlendState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string value when reading BlendState");
        }

        string? presetName = reader.GetString();
        if (presetName == null)
        {
            throw new JsonException("BlendState preset name cannot be null");
        }

        if (PresetMap.TryGetValue(presetName, out BlendState preset))
        {
            return preset;
        }

        throw new JsonException($"Unknown BlendState preset: '{presetName}'. Valid presets are: {string.Join(", ", PresetMap.Keys)}");
    }

    /// <summary>
    /// Writes a BlendState value as a JSON string.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The BlendState value to write.</param>
    /// <param name="options">The serializer options.</param>
    /// <exception cref="JsonException">Thrown when the BlendState value doesn't match any known preset.</exception>
    public override void Write(Utf8JsonWriter writer, BlendState value, JsonSerializerOptions options)
    {
        if (ReverseMap.TryGetValue(value, out string? presetName))
        {
            writer.WriteStringValue(presetName);
        }
        else
        {
            throw new JsonException($"Cannot serialize BlendState: value doesn't match any known preset");
        }
    }
}