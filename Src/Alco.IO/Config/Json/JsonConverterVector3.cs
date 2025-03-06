using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.IO;

/// <summary>
/// JSON converter for System.Numerics.Vector3 type.
/// Serializes Vector3 as a JSON array of three float values in the format [x, y, z].
/// </summary>
public unsafe class JsonConverterVector3 : BaseJsonConverterVector<Vector3>
{
    /// <summary>
    /// Reads a JSON array and converts it to a Vector3.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Vector3).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Vector3 instance containing the deserialized x, y and z values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float* array = stackalloc float[3];
        ReadFloatArray(ref reader, array, 3);
        return new Vector3(array[0], array[1], array[2]);
    }

    /// <summary>
    /// Writes a Vector3 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Vector3 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        float* ptr = (float*)&value;
        WriteFloatArray(writer, ptr, 3);
    }
}