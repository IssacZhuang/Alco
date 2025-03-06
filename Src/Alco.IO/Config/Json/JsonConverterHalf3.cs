using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.IO;

/// <summary>
/// JSON converter for Alco.Half3 type.
/// Serializes Half3 as a JSON array of three float values in the format [x, y, z].
/// </summary>
public unsafe class JsonConverterHalf3 : BaseJsonConverterHalfVector<Half3>
{
    /// <summary>
    /// Reads a JSON array and converts it to a Half3.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Half3).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Half3 instance containing the deserialized x, y, and z values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override Half3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Half* array = stackalloc Half[3];
        ReadHalfArray(ref reader, array, 3);
        return new Half3(array[0], array[1], array[2]);
    }

    /// <summary>
    /// Writes a Half3 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Half3 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Half3 value, JsonSerializerOptions options)
    {
        // The Half3 can be used as a Half array based on the memory layout of the struct
        Half* ptr = (Half*)&value;
        WriteHalfArray(writer, ptr, 3);
    }
}