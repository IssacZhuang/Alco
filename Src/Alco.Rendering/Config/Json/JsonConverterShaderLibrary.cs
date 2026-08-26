using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Rendering;

/// <summary>
/// Json converter for <see cref="ShaderLibrary"/>: a material asset references its
/// surface by module name string; the reference resolves (and validates) through the
/// shader system at deserialization time, so a typo fails at asset load with the
/// file's context instead of at first material compile.
/// </summary>
public class JsonConverterShaderLibrary : JsonConverter<ShaderLibrary>
{
    private readonly ShaderSystem _shaderSystem;

    public JsonConverterShaderLibrary(ShaderSystem shaderSystem)
    {
        _shaderSystem = shaderSystem;
    }

    public override ShaderLibrary? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a shader library module name string.");
        }
        string? name = reader.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        try
        {
            return _shaderSystem.GetLibrary(name.Trim());
        }
        catch (Exception exception)
        {
            // Same load-time failure contract as shader references: a typoed
            // module fails at load with the file's context.
            throw new JsonException(
                $"Shader library '{name.Trim()}' failed to resolve: {exception.Message}", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ShaderLibrary value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
