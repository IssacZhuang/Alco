using System.Reflection;
using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace BenchmarkFramework;

/// <summary>
/// Runs benchmarks from an assembly using the supplied configuration.
/// </summary>
public class Runner
{
    /// <summary>
    /// Runs all benchmarks or the benchmark types named in the arguments.
    /// </summary>
    /// <param name="assembly">The assembly containing the benchmarks.</param>
    /// <param name="config">The benchmark configuration.</param>
    /// <param name="args">The benchmark type names to run, or empty to run all benchmarks.</param>
    public static void Run(Assembly assembly, IConfig config, string[] args)
    {
        Summary[] summaries = [];

        if (args.Length <= 0)
        {
            //run all benchmarks in assembly
            summaries = BenchmarkRunner.Run(assembly, config);
        }
        else
        {
            Type[] types = assembly.GetTypes();
            Dictionary<string, Type> typeMap = new Dictionary<string, Type>();
            foreach (var type in types)
            {
                typeMap[type.Name] = type;
            }

            List<Summary> tmp = [];
            foreach (var arg in args)
            {
                if (typeMap.TryGetValue(arg, out var type))
                {
                    CustomConfigParamAttribute? configParam = type.GetCustomAttribute<CustomConfigParamAttribute>();
                    if (configParam != null)
                    {
                        var summary = BenchmarkRunner.Run(type, configParam.GetConfig());
                        tmp.Add(summary);
                    }
                    else
                    {
                        var summary = BenchmarkRunner.Run(type, config);
                        tmp.Add(summary);
                    }
                }
                else
                {
                    throw new Exception($"Benchmark {arg} not found");
                }
            }
            summaries = tmp.ToArray();
        }


        ILogger logger = ConsoleLogger.Default;
        foreach (var summary in summaries)
        {
            MarkdownExporter.Console.ExportToLog(summary, logger);
            ConclusionHelper.Print(logger, summary.BenchmarksCases.First().Config.GetCompositeAnalyser().Analyse(summary).ToList());
        }
    }
}