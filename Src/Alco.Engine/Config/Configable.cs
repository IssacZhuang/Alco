using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class Configable
{
    /// <summary>
    /// The unique identifier of the config, it could be path or Guid
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Whether the config is a sub resource.
    /// It will be serialized into a same json file with its parent if it is true. 
    /// Otherwise, it will be serialized as reference.
    /// </summary>
    [JsonIgnore]
    public bool IsSubResource { get; set; } = false;
}

