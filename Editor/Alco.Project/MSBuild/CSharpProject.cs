using Microsoft.Build.Execution;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;

namespace Alco.Project;

public class CSharpProject
{
    public Microsoft.Build.Evaluation.Project MSBuildProject { get; set; } = null!;
    public string FullPath => MSBuildProject.FullPath;

    public CSharpProject(string path)
    {
        MSBuildProject = new Microsoft.Build.Evaluation.Project(path);
    }

    public BuildResult BuildAssembly(BuildOption option)
    {
        MSBuildLocator.RegisterDefaults();

        Dictionary<string, string?> globalProperties = option.GetGlobalProperties();

        BuildParameters buildParameters = new BuildParameters()
        {
            GlobalProperties = globalProperties,
            Loggers = [new Microsoft.Build.Logging.ConsoleLogger()],
        };

        BuildRequestData buildRequestData = new BuildRequestData(
            MSBuildProject.FullPath,
            globalProperties,
            null,
            ["Build"],
            null);

        return BuildManager.DefaultBuildManager.Build(buildParameters, buildRequestData);
    }

}

