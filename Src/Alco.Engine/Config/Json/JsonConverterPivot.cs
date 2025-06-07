using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.Pivot type.
/// Supports two formats:
/// 1. String format: "Center", "LeftTop", "RightBottom", etc. (using static readonly presets)
/// 2. Object format: {"x": 0.5, "y": -0.5} (using BaseJsonConverterVector ReadFloatObject)
/// </summary>
public unsafe class JsonConverterPivot : BaseJsonConverterVector<Pivot>
{
    private static readonly string[] ComponentNames = { "x", "y" };

    /// <summary>
    /// Reads a JSON value and converts it to a Pivot.
    /// Supports both string format (preset names) and object format.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Pivot).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Pivot instance containing the deserialized values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected formats.</exception>
    public override Pivot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? presetName = reader.GetString();
            if (presetName != null)
            {
                return presetName.ToLowerInvariant() switch
                {
                    "center" => Pivot.Center,
                    "leftcenter" => Pivot.LeftCenter,
                    "rightcenter" => Pivot.RightCenter,
                    "centertop" => Pivot.CenterTop,
                    "centerbottom" => Pivot.CenterBottom,
                    "lefttop" => Pivot.LeftTop,
                    "leftbottom" => Pivot.LeftBottom,
                    "righttop" => Pivot.RightTop,
                    "rightbottom" => Pivot.RightBottom,
                    _ => throw new JsonException($"Unknown Pivot preset name: '{presetName}'")
                };
            }
            throw new JsonException("Pivot preset name cannot be null");
        }

        // Use object format
        float* array = stackalloc float[2];
        ReadFloatObject(ref reader, array, ComponentNames);
        return new Pivot(array[0], array[1]);
    }

    /// <summary>
    /// Writes a Pivot value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Pivot value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Pivot value, JsonSerializerOptions options)
    {
        // Fall back to object format
        float* ptr = (float*)&value.value;
        WriteFloatObject(writer, ptr, ComponentNames);
    }
}