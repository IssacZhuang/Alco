using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.IO;

/// <summary>
/// JSON converter for Alco.Half4 type.
/// Serializes Half4 as a JSON array of four float values in the format [x, y, z, w].
/// </summary>
public unsafe class JsonConverterHalf4 : BaseJsonConverterHalfVector<Half4>
{
    /// <summary>
    /// Reads a JSON array and converts it to a Half4.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Half4).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Half4 instance containing the deserialized x, y, z, and w values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override Half4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Half* array = stackalloc Half[4];
        ReadHalfArray(ref reader, array, 4);
        return new Half4(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes a Half4 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Half4 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Half4 value, JsonSerializerOptions options)
    {
        // The Half4 can be used as a Half array based on the memory layout of the struct
        Half* ptr = (Half*)&value;
        WriteHalfArray(writer, ptr, 4);
    }
}