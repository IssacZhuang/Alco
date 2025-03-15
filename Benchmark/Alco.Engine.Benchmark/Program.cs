using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Alco.Engine.Benchmark;
using BenchmarkDotNet.Reports;




IConfig config = new BenchmarkConfig();

Summary[] summaries = [];

if (args.Length <= 0)
{
    //run all benchmarks in assembly
    summaries = BenchmarkRunner.Run(typeof(Program).Assembly, config);
}
else
{
    Type[] types = typeof(Program).Assembly.GetTypes();
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
            var summary = BenchmarkRunner.Run(type, config);
            tmp.Add(summary);
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


