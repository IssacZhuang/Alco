using System;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.uint4 type.
/// Serializes uint4 as a JSON array of four unsigned integer values in the format [x, y, z, w].
/// </summary>
public unsafe class JsonConverterUInt4 : BaseJsonConverterUIntVector<uint4>
{
    /// <summary>
    /// Reads a JSON array and converts it to a uint4.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (uint4).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A uint4 instance containing the deserialized x, y, z, and w values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override uint4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        uint* array = stackalloc uint[4];
        ReadUIntArray(ref reader, array, 4);
        return new uint4(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes a uint4 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The uint4 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, uint4 value, JsonSerializerOptions options)
    {
        // The uint4 can be used as a uint array based on the memory layout of the struct
        uint* ptr = (uint*)&value;
        WriteUIntArray(writer, ptr, 4);
    }
}