using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Alco.Benchmark;
using BenchmarkDotNet.Reports;
using BenchmarkFramework;

IConfig config = new DefaultBenchmarkConfig();

Runner.Run(typeof(Program).Assembly, config, args);
