using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Base type for configs that can be registered and referenced in a config database.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class Configable
{
    /// <summary>
    /// The unique identifier of the config, it could be path or Guid.
    /// </summary>
    [JsonPropertyOrder(-2)]
    public string Id { get; set; } = string.Empty;
}

