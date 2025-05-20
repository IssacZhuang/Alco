using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.Half2 type.
/// Serializes Half2 as a JSON array of two float values in the format [x, y].
/// </summary>
public unsafe class JsonConverterHalf2 : BaseJsonConverterHalfVector<Half2>
{
    /// <summary>
    /// Reads a JSON array and converts it to a Half2.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Half2).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Half2 instance containing the deserialized x and y values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override Half2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Half* array = stackalloc Half[2];
        ReadHalfArray(ref reader, array, 2);
        return new Half2(array[0], array[1]);
    }

    /// <summary>
    /// Writes a Half2 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Half2 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Half2 value, JsonSerializerOptions options)
    {
        // The Half2 can be used as a Half array based on the memory layout of the struct
        Half* ptr = (Half*)&value;
        WriteHalfArray(writer, ptr, 2);
    }
}