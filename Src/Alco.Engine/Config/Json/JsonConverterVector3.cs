using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for System.Numerics.Vector3 type.
/// Serializes Vector3 as a JSON object of three float values in the format {"x": 1.0, "y": 2.0, "z": 3.0}.
/// </summary>
public unsafe class JsonConverterVector3 : BaseJsonConverterVector<Vector3>
{
    private static readonly string[] ComponentNames = { "x", "y", "z" };

    /// <summary>
    /// Reads a JSON object and converts it to a Vector3.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Vector3).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Vector3 instance containing the deserialized x, y and z values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected object format.</exception>
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float* array = stackalloc float[3];
        ReadFloatObject(ref reader, array, ComponentNames);
        return new Vector3(array[0], array[1], array[2]);
    }

    /// <summary>
    /// Writes a Vector3 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Vector3 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        float* ptr = (float*)&value;
        WriteFloatObject(writer, ptr, ComponentNames);
    }
}