using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// JSON converter for DepthStencilState type.
/// Supports string format that maps to static readonly presets: "None", "Write", "Read", "Default"
/// </summary>
public class JsonConverterDepthStencilState : JsonConverter<DepthStencilState>
{
    private static readonly Dictionary<string, DepthStencilState> PresetMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "None", DepthStencilState.None },
        { "Write", DepthStencilState.Write },
        { "Read", DepthStencilState.Read },
        { "Default", DepthStencilState.Default }
    };

    private static readonly Dictionary<DepthStencilState, string> ReverseMap = new()
    {
        { DepthStencilState.None, "None" },
        { DepthStencilState.Write, "Write" },
        { DepthStencilState.Read, "Read" },
        { DepthStencilState.Default, "Default" }
    };

    /// <summary>
    /// Reads a JSON string value and converts it to a DepthStencilState preset.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (DepthStencilState).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A DepthStencilState instance from the preset mapping.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or the preset name is not recognized.</exception>
    public override DepthStencilState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string value when reading DepthStencilState");
        }

        string? presetName = reader.GetString();
        if (presetName == null)
        {
            throw new JsonException("DepthStencilState preset name cannot be null");
        }

        if (PresetMap.TryGetValue(presetName, out DepthStencilState preset))
        {
            return preset;
        }

        throw new JsonException($"Unknown DepthStencilState preset: '{presetName}'. Valid presets are: {string.Join(", ", PresetMap.Keys)}");
    }

    /// <summary>
    /// Writes a DepthStencilState value as a JSON string.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The DepthStencilState value to write.</param>
    /// <param name="options">The serializer options.</param>
    /// <exception cref="JsonException">Thrown when the DepthStencilState value doesn't match any known preset.</exception>
    public override void Write(Utf8JsonWriter writer, DepthStencilState value, JsonSerializerOptions options)
    {
        if (ReverseMap.TryGetValue(value, out string? presetName))
        {
            writer.WriteStringValue(presetName);
        }
        else
        {
            throw new JsonException($"Cannot serialize DepthStencilState: value doesn't match any known preset");
        }
    }
}