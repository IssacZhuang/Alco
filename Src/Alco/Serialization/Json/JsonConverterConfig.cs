using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco;

public class JsonConverterConfig : JsonConverter<IJsonConfig>
{
    public override IJsonConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 0)
        {
            //read $type
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, IJsonConfig value, JsonSerializerOptions options)
    {

    }
}

