using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace BenchmarkFramework;

/// <summary>
/// Default BenchmarkDotNet configuration using three warmup and fifteen measurement iterations.
/// </summary>
public class DefaultBenchmarkConfig : ManualConfig
{
    /// <summary>
    /// Initializes the default benchmark configuration.
    /// </summary>
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
