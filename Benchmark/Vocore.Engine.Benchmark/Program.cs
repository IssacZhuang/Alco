using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Vocore.Engine.Benchmark;

IConfig config = new BenchmarkConfig();

//run all benchmarks in assembly
var summarys = BenchmarkRunner.Run(typeof(Program).Assembly, config);



ILogger logger = ConsoleLogger.Default;
foreach (var summary in summarys)
{
    MarkdownExporter.Console.ExportToLog(summary, logger);
    ConclusionHelper.Print(logger, summary.BenchmarksCases.First().Config.GetCompositeAnalyser().Analyse(summary).ToList());
}


