using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Vocore.Benchmark;


//run all benchmarks in assembly
var summarys = BenchmarkRunner.Run(typeof(Program).Assembly, new BenchmarkConfig());
