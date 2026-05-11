using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace BenchmarkFramework;

public class DefaultBenchmarkConfig : ManualConfig
{
    public DefaultBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(15)
            .WithInvocationCount(256))
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance);
    }
}
