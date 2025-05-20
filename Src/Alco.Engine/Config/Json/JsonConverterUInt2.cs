using System;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.uint2 type.
/// Serializes uint2 as a JSON array of two unsigned integer values in the format [x, y].
/// </summary>
public unsafe class JsonConverterUInt2 : BaseJsonConverterUIntVector<uint2>
{
    /// <summary>
    /// Reads a JSON array and converts it to a uint2.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (uint2).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A uint2 instance containing the deserialized x and y values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override uint2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        uint* array = stackalloc uint[2];
        ReadUIntArray(ref reader, array, 2);
        return new uint2(array[0], array[1]);
    }

    /// <summary>
    /// Writes a uint2 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The uint2 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, uint2 value, JsonSerializerOptions options)
    {
        // The uint2 can be used as a uint array based on the memory layout of the struct
        uint* ptr = (uint*)&value;
        WriteUIntArray(writer, ptr, 2);
    }
}