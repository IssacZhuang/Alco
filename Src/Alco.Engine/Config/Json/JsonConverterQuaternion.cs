using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for System.Numerics.Quaternion type.
/// Serializes Quaternion as a JSON array of four float values in the format [x, y, z, w].
/// </summary>
public unsafe class JsonConverterQuaternion : BaseJsonConverterVector<Quaternion>
{
    /// <summary>
    /// Reads a JSON array and converts it to a Quaternion.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Quaternion).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Quaternion instance containing the deserialized x, y, z and w values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float* array = stackalloc float[4];
        ReadFloatArray(ref reader, array, 4);
        return new Quaternion(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes a Quaternion value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Quaternion value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
    {
        float* ptr = (float*)&value;
        WriteFloatArray(writer, ptr, 4);
    }
}