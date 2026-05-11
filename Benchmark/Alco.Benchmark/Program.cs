using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Alco.Benchmark;
using BenchmarkFramework;

if (args.Contains("--profile"))
{
    ProfileImageDecode.Run(args);
    return;
}

IConfig config = new DefaultBenchmarkConfig();

Runner.Run(typeof(Program).Assembly, config, args);
