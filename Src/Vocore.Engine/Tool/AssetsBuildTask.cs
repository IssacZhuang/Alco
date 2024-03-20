using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Vocore.Engine.Tool;

public class AssetsBuildTask : Microsoft.Build.Utilities.Task
{
    private const string DefaultPackageName = "Assets";

    [Required]
    public string? AssetsDir { get; set; }

    [Required]
    public string? OutputDir { get; set; }

    public string? PackageName { get; set; }

    public override bool Execute()
    {
        if (string.IsNullOrEmpty(AssetsDir))
        {
            Log.LogError("AssetsDir are required");
            return false;
        }

        if (string.IsNullOrEmpty(OutputDir))
        {
            Log.LogError("OutputDir are required");
            return false;
        }

        Log.LogMessage(MessageImportance.High, "Hello from Vocore.Rendering.Tool.AssetsBuildTask");
        return true;
    }
}
