using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for System.Numerics.Vector4 type.
/// Serializes Vector4 as a JSON object of four float values in the format {"x": 1.0, "y": 2.0, "z": 3.0, "w": 4.0}.
/// </summary>
public unsafe class JsonConverterVector4 : BaseJsonConverterVector<Vector4>
{
    private static readonly string[] ComponentNames = { "x", "y", "z", "w" };

    /// <summary>
    /// Reads a JSON object and converts it to a Vector4.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Vector4).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Vector4 instance containing the deserialized x, y, z and w values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected object format.</exception>
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float* array = stackalloc float[4];
        ReadFloatObject(ref reader, array, ComponentNames);
        return new Vector4(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes a Vector4 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Vector4 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        float* ptr = (float*)&value;
        WriteFloatObject(writer, ptr, ComponentNames);
    }
}