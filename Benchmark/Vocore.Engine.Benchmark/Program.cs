using BenchmarkDotNet.Running;

//run all benchmarks in assembly
var summarys = BenchmarkRunner.Run(typeof(Program).Assembly);
