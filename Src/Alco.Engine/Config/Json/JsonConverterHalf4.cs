using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.Half4 type.
/// Serializes Half4 as a JSON object of four float values in the format {"x": 1.0, "y": 2.0, "z": 3.0, "w": 4.0}.
/// </summary>
public unsafe class JsonConverterHalf4 : BaseJsonConverterHalfVector<Half4>
{
    private static readonly string[] ComponentNames = { "x", "y", "z", "w" };

    /// <summary>
    /// Reads a JSON object and converts it to a Half4.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Half4).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Half4 instance containing the deserialized x, y, z, and w values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected object format.</exception>
    public override Half4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Half* array = stackalloc Half[4];
        ReadHalfObject(ref reader, array, ComponentNames);
        return new Half4(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes a Half4 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Half4 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Half4 value, JsonSerializerOptions options)
    {
        // The Half4 can be used as a Half array based on the memory layout of the struct
        Half* ptr = (Half*)&value;
        WriteHalfObject(writer, ptr, ComponentNames);
    }
}