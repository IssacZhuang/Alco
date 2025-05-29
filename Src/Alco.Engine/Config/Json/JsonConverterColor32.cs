using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Engine;

public unsafe class JsonConverterColor32 : BaseJsonConverterVector<Color32>
{
    public override Color32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? hex = reader.GetString();
            if (hex != null && Color32.TryParse(hex, out Color32 color))
            {
                return color;
            }
            throw new JsonException("Invalid hex color string");
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array when reading VectorN");
        }


        byte* array = stackalloc byte[4];
        reader.Read();

        for (int i = 0; i < 3; i++)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {

                array[i] = reader.GetByte();
                reader.Read();
            }
            else
            {
                throw new JsonException($"Expected as least 3 float values in the array, Got {i} float values.");
            }
        }

        //last value is alpha (optional), default is 1.0f
        array[3] = 255;
        if (reader.TokenType == JsonTokenType.Number)
        {
            array[3] = reader.GetByte();
            reader.Read();
        }


        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Expected end of array when reading VectorN");
        }

        return new Color32(array[0], array[1], array[2], array[3]);
    }

    public override void Write(Utf8JsonWriter writer, Color32 value, JsonSerializerOptions options)
    {
        byte* ptr = (byte*)&value;
        writer.WriteStartArray();
        for (int i = 0; i < 4; i++)
        {
            writer.WriteNumberValue(ptr[i]);
        }
        writer.WriteEndArray();
    }
}
