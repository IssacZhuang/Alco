
using System.Runtime.InteropServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;

namespace Alco.Project;

public class DotnetSolution
{
    public SolutionFile SolutionFile { get; }
    public string FilePath { get; }

    public DotnetSolution(SolutionFile solutionFile, string filename)
    {
        SolutionFile = solutionFile;
        FilePath = filename;
    }

    private const string Configuration = "Configuration";
    private const string RuntimeIdentifier = "RuntimeIdentifier";
    private const string Optimize = "Optimize";

    public BuildResult BuildAssembly(string? projectName, BuildOption option)
    {
        MSBuildLocator.RegisterDefaults();

        string projectPath = projectName ?? Path.GetFileNameWithoutExtension(FilePath);

        ProjectInSolution? projectInSolution = SolutionFile.ProjectsInOrder.FirstOrDefault(p => p.ProjectName == projectName);
        if (projectInSolution == null)
        {
            throw new Exception($"Project {projectName} not found");
        }

        ProjectCollection projectCollection = new ProjectCollection();
        Microsoft.Build.Evaluation.Project project = projectCollection.LoadProject(projectInSolution.AbsolutePath);

        Dictionary<string, string?> globalProperties = new Dictionary<string, string?>();
        globalProperties[Configuration] = option.GetConfiguration();
        globalProperties[RuntimeIdentifier] = option.GetRuntimeIdentifier();
        globalProperties[Optimize] = option.GetOptimize();

        BuildParameters buildParameters = new BuildParameters(projectCollection)
        {
            GlobalProperties = globalProperties,
            Loggers = [new ConsoleLogger()],
        };

        BuildRequestData buildRequestData = new BuildRequestData(
            project.FullPath,
            globalProperties,
            null,
            ["Build"],
            null);

        return BuildManager.DefaultBuildManager.Build(buildParameters, buildRequestData);
    }

    

}

