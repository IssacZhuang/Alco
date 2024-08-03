using System.Linq;

namespace DirectXShaderCompiler.NET;

#pragma warning disable 1591

/// <summary>
/// The shader program types that DXC can consume
/// </summary>
public enum ShaderType : ushort
{
    Fragment = 100,
    Vertex = 200,
    Geometry = 300,
    Hull = 400,
    Domain = 500,
    Compute = 600,
    Library = 700,
    Mesh = 800,
    Amplification = 900,
}

/// <summary>
/// The shader profile for DXC to consume
/// </summary>
public enum ShaderProfile : ushort
{
    Fragment_6_0 = ShaderType.Fragment + 60, 
    Fragment_6_1 = ShaderType.Fragment + 61, 
    Fragment_6_2 = ShaderType.Fragment + 62, 
    Fragment_6_3 = ShaderType.Fragment + 63, 
    Fragment_6_4 = ShaderType.Fragment + 64, 
    Fragment_6_5 = ShaderType.Fragment + 65,
    Fragment_6_6 = ShaderType.Fragment + 66, 
    Fragment_6_7 = ShaderType.Fragment + 67,
    Fragment_6_8 = ShaderType.Fragment + 68,

    Vertex_6_0 = ShaderType.Vertex + 60,
    Vertex_6_1 = ShaderType.Vertex + 61,
    Vertex_6_2 = ShaderType.Vertex + 62,
    Vertex_6_3 = ShaderType.Vertex + 63,
    Vertex_6_4 = ShaderType.Vertex + 64,
    Vertex_6_5 = ShaderType.Vertex + 65,
    Vertex_6_6 = ShaderType.Vertex + 66, 
    Vertex_6_7 = ShaderType.Vertex + 67,
    Vertex_6_8 = ShaderType.Vertex + 68,

    Geometry_6_0 = ShaderType.Geometry + 60,
    Geometry_6_1 = ShaderType.Geometry + 61,
    Geometry_6_2 = ShaderType.Geometry + 62,
    Geometry_6_3 = ShaderType.Geometry + 63,
    Geometry_6_4 = ShaderType.Geometry + 64,
    Geometry_6_5 = ShaderType.Geometry + 65,
    Geometry_6_6 = ShaderType.Geometry + 66, 
    Geometry_6_7 = ShaderType.Geometry + 67,
    Geometry_6_8 = ShaderType.Geometry + 68,
    
    Hull_6_0 = ShaderType.Hull + 60,
    Hull_6_1 = ShaderType.Hull + 61,
    Hull_6_2 = ShaderType.Hull + 62,
    Hull_6_3 = ShaderType.Hull + 63,
    Hull_6_4 = ShaderType.Hull + 64,
    Hull_6_5 = ShaderType.Hull + 65,
    Hull_6_6 = ShaderType.Hull + 66, 
    Hull_6_7 = ShaderType.Hull + 67,
    Hull_6_8 = ShaderType.Hull + 68,
    
    Domain_6_0 = ShaderType.Domain + 60,
    Domain_6_1 = ShaderType.Domain + 61,
    Domain_6_2 = ShaderType.Domain + 62,
    Domain_6_3 = ShaderType.Domain + 63,
    Domain_6_4 = ShaderType.Domain + 64,
    Domain_6_5 = ShaderType.Domain + 65,
    Domain_6_6 = ShaderType.Domain + 66, 
    Domain_6_7 = ShaderType.Domain + 67,
    Domain_6_8 = ShaderType.Domain + 68,
    
    Compute_6_0 = ShaderType.Compute + 60,
    Compute_6_1 = ShaderType.Compute + 61,
    Compute_6_2 = ShaderType.Compute + 62,
    Compute_6_3 = ShaderType.Compute + 63,
    Compute_6_4 = ShaderType.Compute + 64,
    Compute_6_5 = ShaderType.Compute + 65,
    Compute_6_6 = ShaderType.Compute + 66, 
    Compute_6_7 = ShaderType.Compute + 67,
    Compute_6_8 = ShaderType.Compute + 68,
    
    Library_6_1 = ShaderType.Library + 61,
    Library_6_2 = ShaderType.Library + 62,
    Library_6_3 = ShaderType.Library + 63,
    Library_6_4 = ShaderType.Library + 64,
    Library_6_5 = ShaderType.Library + 65,
    Library_6_6 = ShaderType.Library + 66, 
    Library_6_7 = ShaderType.Library + 67,
    Library_6_8 = ShaderType.Library + 68,

    Mesh_6_5 = ShaderType.Mesh + 65,
    Mesh_6_6 = ShaderType.Mesh + 66,
    Mesh_6_7 = ShaderType.Mesh + 67,
    Mesh_6_8 = ShaderType.Mesh + 68,

    Amplification_6_5 = ShaderType.Amplification + 65,
    Amplification_6_6 = ShaderType.Amplification + 66, 
    Amplification_6_7 = ShaderType.Amplification + 67,
    Amplification_6_8 = ShaderType.Amplification + 68,
}


public static class ShaderProfileExtensions
{  
    private static readonly Dictionary<ushort, ShaderProfile> profileValues = Enum.GetValues<ShaderProfile>().ToDictionary(s => (ushort)s);

    /// <summary>
    /// Connverts the given <see cref="ShaderType"/> into a validated <see cref="ShaderProfile"/> 
    /// </summary>
    /// <param name="type">The shader type to use in the profile</param>
    /// <param name="majorVersion">The profileor version</param>
    /// <param name="minorVersion">The profileor version</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static ShaderProfile ToProfile(this ShaderType type, int majorVersion, int minorVersion)
    {
        int value = (ushort)type; 
        value += Math.Clamp(majorVersion, 0, 9) * 10;
        value += Math.Clamp(minorVersion, 0, 9);

        if (!profileValues.TryGetValue((ushort)value, out ShaderProfile profile))
            throw new ArgumentOutOfRangeException($"Version {majorVersion}.{minorVersion} is not in the range of valid profile valuor shader type: {type}");

        return profile;
    }

    public static string ToString(this ShaderProfile stage)
    {
        ushort stageVal = (ushort)stage;

        ShaderType type = (ShaderType)(stageVal / 100 * 100);
        int majorVersion = stageVal / 10 % 10;
        int minorVersion = stageVal % 10;

        return type switch
        {
            ShaderType.Fragment => "ps",
            ShaderType.Vertex => "vs",
            ShaderType.Geometry => "gs",
            ShaderType.Hull => "hs",
            ShaderType.Domain => "ds",
            ShaderType.Compute => "cs",
            ShaderType.Library => "lib",
            ShaderType.Mesh => "ms",
            ShaderType.Amplification => "as",

            _ => throw new Exception($"Invalid shader stage: {stage}")  
        } + $"_{majorVersion}_{minorVersion}";
    }
}