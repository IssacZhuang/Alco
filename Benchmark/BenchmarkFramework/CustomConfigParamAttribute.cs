using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace BenchmarkFramework;

/// <summary>
/// Configures warmup, iteration, and invocation counts for a benchmark class.
/// </summary>
public class CustomConfigParamAttribute : Attribute
{
    /// <summary>
    /// Gets the warmup iteration count.
    /// </summary>
    public int WarmupCount { get; }
    /// <summary>
    /// Gets the iteration count.
    /// </summary>
    public int IterationCount { get; }
    /// <summary>
    /// Gets the invocation count per iteration.
    /// </summary>
    public int InvocationCount { get; }
    /// <summary>
    /// Initializes the attribute with the specified benchmark counts.
    /// </summary>
    /// <param name="warmupCount">The warmup iteration count.</param>
    /// <param name="iterationCount">The iteration count.</param>
    /// <param name="invocationCount">The invocation count per iteration.</param>
    public CustomConfigParamAttribute(int warmupCount, int iterationCount, int invocationCount)
    {
        WarmupCount = warmupCount;
        IterationCount = iterationCount;
        InvocationCount = invocationCount;
    }

    /// <summary>
    /// Creates a BenchmarkDotNet configuration from the attribute values.
    /// </summary>
    /// <returns>The configured benchmark settings.</returns>
    public IConfig GetConfig()
    {
        return new ManualConfig()
            .AddJob(Job.Default
            .WithWarmupCount(WarmupCount)
            .WithIterationCount(IterationCount)
            .WithInvocationCount(InvocationCount)).
            AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance); ;
    }
}
