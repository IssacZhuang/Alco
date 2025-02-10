using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco;

public class JsonConverterConfig : JsonConverter<IJsonConfig>
{
    private readonly TypeHelper _typeHelper;
    private const string TypeFieldName = "$type";

    public JsonConverterConfig()
    {
        _typeHelper = new TypeHelper();
    }

    public JsonConverterConfig(TypeHelper typeHelper)
    {
        _typeHelper = typeHelper;
    }

    public JsonConverterConfig(params Assembly[] assemblies)
    {
        _typeHelper = new TypeHelper(assemblies);
    }

    /// <summary>
    /// Add assemblies to search for types.
    /// </summary>
    /// <param name="assemblies">The assemblies to add.</param>
    public void AddAssemblies(params ReadOnlySpan<Assembly> assemblies)
    {
        _typeHelper.AddAssemblies(assemblies);
    }

    public override IJsonConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            //todo: resolve cross reference
            throw new NotImplementedException();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading IJsonConfig");
        }

        // Read the first property which should be $type
        reader.Read();
        if (reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != TypeFieldName)
        {
            throw new JsonException($"Expected '{TypeFieldName}' property as first property in object");
        }

        // Read the type name
        reader.Read();
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string value for '{TypeFieldName}' property");
        }

        string? typeName = reader.GetString();
        if (string.IsNullOrEmpty(typeName))
        {
            throw new JsonException($"Type name cannot be null or empty");
        }

        // Find the type
        Type? type = _typeHelper.FindType(typeName);
        if (type == null)
        {
            throw new JsonException($"Type '{typeName}' could not be found in any loaded assemblies");
        }

        // Verify the type implements IJsonConfig
        if (!typeof(IJsonConfig).IsAssignableFrom(type))
        {
            throw new JsonException($"Type '{typeName}' does not implement IJsonConfig");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return (IJsonConfig?)JsonSerializer.Deserialize(document.RootElement, type, options);
    }

    public override void Write(Utf8JsonWriter writer, IJsonConfig value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        var type = value.GetType();

        // Start the JSON object
        writer.WriteStartObject();

        // Write the $type field first
        writer.WriteString(TypeFieldName, type.FullName);

        // Serialize the rest of the object's properties
        var jsonString = JsonSerializer.Serialize(value, type, options);
        using var document = JsonDocument.Parse(jsonString);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name != TypeFieldName) // Skip $type if it exists in the original object
            {
                property.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }
}

