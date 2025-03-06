using System.Runtime.InteropServices;

namespace Alco.Project;

public struct BuildOption
{
    private const string PropertyConfiguration = "Configuration";
    private const string PropertyRuntimeIdentifier = "RuntimeIdentifier";
    private const string PropertyOptimize = "Optimize";

    public static readonly BuildOption Debug = new BuildOption(BuildMode.Debug, BuildPlatform.Auto, false);
    public static readonly BuildOption Release = new BuildOption(BuildMode.Release, BuildPlatform.Auto, true);

    public BuildMode BuildMode;
    public BuildPlatform BuildPlatform;
    public bool Optimize;

    public BuildOption(BuildMode buildMode, BuildPlatform buildPlatform, bool optimize)
    {
        BuildMode = buildMode;
        BuildPlatform = buildPlatform;
        Optimize = optimize;
    }

    public string GetConfiguration( )
    {
        return BuildMode == BuildMode.Debug ? "Debug" : "Release";
    }

    public string GetRuntimeIdentifier()
    {
        BuildPlatform platform = BuildPlatform;
        if (platform == BuildPlatform.Auto)
        {
            platform = GetCurrentBuildPlatform();
        }

        switch (platform)
        {
            case BuildPlatform.Windows_x64:
                return "win-x64";
            case BuildPlatform.Windows_arm64:
                return "win-arm64";
            case BuildPlatform.Linux_x64:
                return "linux-x64";
            case BuildPlatform.Linux_arm64:
                return "linux-arm64";
            case BuildPlatform.MacOS_x64:
                return "osx-x64";
            case BuildPlatform.MacOS_arm64:
                return "osx-arm64";
            default:
                throw new Exception($"Unsupported platform: {platform}");
        }
    }

    public string GetOptimize()
    {
        return Optimize ? "true" : "false";
    }

    public Dictionary<string, string?> GetGlobalProperties()
    {
        return new Dictionary<string, string?>
        {
            { PropertyConfiguration, GetConfiguration() },
            { PropertyRuntimeIdentifier, GetRuntimeIdentifier() },
            { PropertyOptimize, GetOptimize() },
        };
    }

    private static BuildPlatform GetCurrentBuildPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                return BuildPlatform.Windows_arm64;
            }
            else
            {
                return BuildPlatform.Windows_x64;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                return BuildPlatform.Linux_arm64;
            }
            else
            {
                return BuildPlatform.Linux_x64;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                return BuildPlatform.MacOS_arm64;
            }
            else
            {
                return BuildPlatform.MacOS_x64;
            }
        }

        throw new Exception("Unsupported platform");
    }
}
