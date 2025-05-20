using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;

namespace Alco.Engine;

public unsafe class JsonConverterColorFloat : BaseJsonConverterVector<ColorFloat>
{
    public override ColorFloat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array when reading VectorN");
        }


        float* array = stackalloc float[4];
        reader.Read();

        for (int i = 0; i < 3; i++)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {

                array[i] = reader.GetSingle();
                reader.Read();
            }
            else
            {
                throw new JsonException($"Expected as least 3 float values in the array, Got {i} float values.");
            }
        }

        //last value is alpha (optional), default is 1.0f
        array[3] = 1.0f;
        if (reader.TokenType == JsonTokenType.Number)
        {
            array[3] = reader.GetSingle();
            reader.Read();
        }
        

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Expected end of array when reading VectorN");
        }

        return new ColorFloat(array[0], array[1], array[2], array[3]);
    }
        

    public override void Write(Utf8JsonWriter writer, ColorFloat value, JsonSerializerOptions options)
    {
        float* ptr = (float*)&value;
        WriteFloatArray(writer, ptr, 4);
    }
}
