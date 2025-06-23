using System;
using System.Text.Json;
using Alco;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.Padding4 type.
/// Serializes Padding4 as a JSON object of four float values in the format {"left": 10.0, "top": 5.0, "right": 10.0, "bottom": 5.0}.
/// Also supports a single number value which will be applied to all edges.
/// </summary>
public unsafe class JsonConverterPadding : BaseJsonConverterVector<Padding>
{
    private static readonly string[] ComponentNames = { "left", "top", "right", "bottom" };

    /// <summary>
    /// Reads a JSON object and converts it to a Padding4.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Padding4).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Padding4 instance containing the deserialized left, top, right, and bottom values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected object format.</exception>
    public override Padding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float* array = stackalloc float[4];
        ReadFloatObject(ref reader, array, ComponentNames);
        return new Padding(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes a Padding4 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Padding4 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Padding value, JsonSerializerOptions options)
    {
        // Use the internal Vector4 value for efficient memory access
        float* ptr = (float*)&value.value;
        WriteFloatObject(writer, ptr, ComponentNames);
    }
}