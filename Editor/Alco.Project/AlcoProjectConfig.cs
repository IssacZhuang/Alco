using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Project;

/// <summary>
/// The config file for the project. 
/// </summary>
public class AlcoProjectConfig
{
    public List<string> AssetsPaths { get; set; } = new List<string>();
}
