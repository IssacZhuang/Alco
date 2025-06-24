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
}

