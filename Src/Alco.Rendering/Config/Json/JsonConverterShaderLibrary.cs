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

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterShaderLibrary"/> class.
    /// </summary>
    /// <param name="shaderSystem">The shader system used to resolve shader libraries.</param>
    public JsonConverterShaderLibrary(ShaderSystem shaderSystem)
    {
        _shaderSystem = shaderSystem;
    }

    /// <summary>
    /// Reads a shader library module name and resolves it through the shader system.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the library reference.</param>
    /// <param name="typeToConvert">The target type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The resolved shader library, or null for an empty or whitespace name.</returns>
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

    /// <summary>
    /// Writes the shader library's module name.
    /// </summary>
    /// <param name="writer">The target JSON writer.</param>
    /// <param name="value">The shader library to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, ShaderLibrary value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
