using System.Text.Json.Nodes;

namespace Alco.Engine;

/// <summary>
/// Interface for JSON converters that can provide their own JSON Schema.
/// When a custom <see cref="System.Text.Json.Serialization.JsonConverter{T}"/>
/// cannot be introspected by <c>JsonSchemaExporter</c>, implementing this interface
/// allows the converter to declare its schema directly.
/// </summary>
public interface IJsonSchemaProvider
{
    /// <summary>
    /// Returns the JSON Schema for the type handled by this converter.
    /// </summary>
    /// <returns>A <see cref="JsonNode"/> representing the JSON Schema.</returns>
    JsonNode GetSchema();
}
