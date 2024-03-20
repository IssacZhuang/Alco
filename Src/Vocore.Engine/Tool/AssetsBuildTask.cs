using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Vocore.Engine.Tool;

public class AssetsBuildTask : Microsoft.Build.Utilities.Task
{
    public override bool Execute()
    {
        
        Log.LogMessage(MessageImportance.High, "Hello from Vocore.Rendering.Tool.AssetsBuildTask");
        return true;
    }
}
