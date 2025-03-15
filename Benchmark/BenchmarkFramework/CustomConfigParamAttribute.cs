using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace BenchmarkFramework;

public class CustomConfigParamAttribute : Attribute
{
    public int WarmupCount { get; }
    public int IterationCount { get; }
    public int InvocationCount { get; }
    public CustomConfigParamAttribute(int warmupCount, int iterationCount, int invocationCount)
    {
        WarmupCount = warmupCount;
        IterationCount = iterationCount;
        InvocationCount = invocationCount;
    }

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
