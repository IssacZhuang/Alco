using System;
using System.Threading;
using System.Threading.Tasks;
using Alco;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks JobScheduler across dispatch latency, throughput, skewed workloads,
/// multi-batch barrier merging, DAG pipelines, and lane contention isolation.
/// </summary>
[MemoryDiagnoser]
[CustomConfigParam(warmupCount: 3, iterationCount: 10, invocationCount: 16)]
public class BenchmarkJobScheduler
{
    private const int DispatchN = 64;
    private const int DispatchRepeats = 200;

    private const int ThroughputN = 10_000;
    private const int LightRepeats = 20;
    private const int MediumRepeats = 5;

    private const int SkewedN = 2048;
    private const int SkewedSplit = 1920;
    private const int SkewedRepeats = 10;

    private const int MultiBatchN = 256;
    private const int MultiBatchRepeats = 20;

    private const int DagRepeats = 100;

    private const int ContentionN = 512;
    private const int ContentionRepeats = 20;

    private JobScheduler _schedulerHelp = null!;
    private JobScheduler _schedulerNoHelp = null!;

    private int[] _srcInt = null!;
    private int[] _dstInt = null!;
    private float[] _srcFloat = null!;
    private float[] _dstFloat = null!;
    private float[] _dstA = null!;
    private float[] _dstB = null!;
    private float[] _dstC = null!;

    private ReusableLightTask _reusableDispatch = null!;
    private ReusableLightTask _reusableLight = null!;
    private ReusableMediumTask _reusableMedium = null!;
    private ReusableSkewedTask _reusableSkewed = null!;
    private ReusableMediumTask _reusableMultiA = null!;
    private ReusableMediumTask _reusableMultiB = null!;
    private ReusableMediumTask _reusableMultiC = null!;
    private ReusableMediumTask _reusableContention = null!;

    private LightBatchJob _jobDispatchBatch = null!;
    private LightRangeJob _jobDispatchRange = null!;
    private LightBatchJob _jobLightBatch = null!;
    private LightRangeJob _jobLightRange = null!;
    private MediumBatchJob _jobMediumBatch = null!;
    private MediumRangeJob _jobMediumRange = null!;
    private SkewedRangeJob _jobSkewedRange = null!;
    private MediumRangeJob _jobMultiA = null!;
    private MediumRangeJob _jobMultiB = null!;
    private MediumRangeJob _jobMultiC = null!;
    private MediumRangeJob _jobContention = null!;

    private EmptySingleJob _dagA = null!;
    private EmptySingleJob _dagB1 = null!;
    private EmptySingleJob _dagB2 = null!;
    private EmptySingleJob _dagB3 = null!;
    private EmptySingleJob _dagC = null!;
    private EmptySingleJob _dagD1 = null!;
    private EmptySingleJob _dagD2 = null!;
    private EmptySingleJob _dagE = null!;

    private volatile bool _stopContention;
    private int _startedContentionWorkers;
    private Task[]? _threadPoolContentionTasks;
    private JobHandle _bgContentionHandle1;
    private JobHandle _bgContentionHandle2;
    private SingleJob _bgSpinJob = null!;
    private float _bgSink;

    /// <summary>Initializes schedulers, buffers, and reusable job instances.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _schedulerHelp = new JobScheduler { EnableMainThreadHelp = true };
        _schedulerNoHelp = new JobScheduler { EnableMainThreadHelp = false };

        _srcInt = new int[ThroughputN];
        _dstInt = new int[ThroughputN];
        _srcFloat = new float[ThroughputN];
        _dstFloat = new float[ThroughputN];
        _dstA = new float[MultiBatchN];
        _dstB = new float[MultiBatchN];
        _dstC = new float[MultiBatchN];

        var random = new FastRandom(12345);
        for (int i = 0; i < ThroughputN; i++)
        {
            _srcInt[i] = (int)random.NextUint(1000);
            _srcFloat[i] = 1f + (_srcInt[i] * 0.01f);
        }

        _reusableDispatch = new ReusableLightTask(_srcInt, _dstInt);
        _reusableLight = new ReusableLightTask(_srcInt, _dstInt);
        _reusableMedium = new ReusableMediumTask(_srcFloat, _dstFloat);
        _reusableSkewed = new ReusableSkewedTask(_srcFloat, _dstFloat);
        _reusableMultiA = new ReusableMediumTask(_srcFloat, _dstA);
        _reusableMultiB = new ReusableMediumTask(_srcFloat, _dstB);
        _reusableMultiC = new ReusableMediumTask(_srcFloat, _dstC);
        _reusableContention = new ReusableMediumTask(_srcFloat, _dstFloat);

        _jobDispatchBatch = new LightBatchJob(DispatchN, _srcInt, _dstInt);
        _jobDispatchRange = new LightRangeJob(DispatchN, _srcInt, _dstInt);
        _jobLightBatch = new LightBatchJob(ThroughputN, _srcInt, _dstInt);
        _jobLightRange = new LightRangeJob(ThroughputN, _srcInt, _dstInt);
        _jobMediumBatch = new MediumBatchJob(ThroughputN, _srcFloat, _dstFloat);
        _jobMediumRange = new MediumRangeJob(ThroughputN, _srcFloat, _dstFloat);
        _jobSkewedRange = new SkewedRangeJob(SkewedN, _srcFloat, _dstFloat);
        _jobMultiA = new MediumRangeJob(MultiBatchN, _srcFloat, _dstA);
        _jobMultiB = new MediumRangeJob(MultiBatchN, _srcFloat, _dstB);
        _jobMultiC = new MediumRangeJob(MultiBatchN, _srcFloat, _dstC);
        _jobContention = new MediumRangeJob(ContentionN, _srcFloat, _dstFloat);

        _dagA = new EmptySingleJob();
        _dagB1 = new EmptySingleJob();
        _dagB2 = new EmptySingleJob();
        _dagB3 = new EmptySingleJob();
        _dagC = new EmptySingleJob();
        _dagD1 = new EmptySingleJob();
        _dagD2 = new EmptySingleJob();
        _dagE = new EmptySingleJob();

        _bgSpinJob = new SingleJob(RunContentionLoop);
    }

    /// <summary>Disposes schedulers and reusable parallel tasks.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _stopContention = true;
        _schedulerHelp.Dispose();
        _schedulerNoHelp.Dispose();

        _reusableDispatch.Dispose();
        _reusableLight.Dispose();
        _reusableMedium.Dispose();
        _reusableSkewed.Dispose();
        _reusableMultiA.Dispose();
        _reusableMultiB.Dispose();
        _reusableMultiC.Dispose();
        _reusableContention.Dispose();
    }

    #region 1. Dispatch & Barrier Latency (200x N=64)

    /// <summary>Baseline repeated tiny Parallel.For dispatch.</summary>
    [Benchmark(Description = "1. Dispatch - Parallel.For")]
    public int Dispatch_ParallelFor()
    {
        for (int r = 0; r < DispatchRepeats; r++)
        {
            Parallel.For(0, DispatchN, i => _dstInt[i] = _srcInt[i] * 2 + 1);
        }
        return _dstInt[0];
    }

    /// <summary>Baseline repeated tiny ReusableBatchTask dispatch.</summary>
    [Benchmark(Description = "1. Dispatch - ReusableBatchTask")]
    public int Dispatch_ReusableBatchTask()
    {
        for (int r = 0; r < DispatchRepeats; r++)
        {
            _reusableDispatch.RunParallel(DispatchN);
        }
        return _dstInt[0];
    }

    /// <summary>JobScheduler IBatchJob with main-thread work-helping enabled.</summary>
    [Benchmark(Description = "1. Dispatch - JobScheduler Batch (Help=true)")]
    public int Dispatch_JobScheduler_Batch_Help()
    {
        for (int r = 0; r < DispatchRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobDispatchBatch);
            _schedulerHelp.Complete(h);
        }
        return _dstInt[0];
    }

    /// <summary>JobScheduler IBatchJob with main-thread work-helping disabled.</summary>
    [Benchmark(Description = "1. Dispatch - JobScheduler Batch (Help=false)")]
    public int Dispatch_JobScheduler_Batch_NoHelp()
    {
        for (int r = 0; r < DispatchRepeats; r++)
        {
            JobHandle h = _schedulerNoHelp.ScheduleBatch(_jobDispatchBatch);
            _schedulerNoHelp.Complete(h);
        }
        return _dstInt[0];
    }

    /// <summary>JobScheduler IBatchRangeJob with main-thread work-helping enabled.</summary>
    [Benchmark(Description = "1. Dispatch - JobScheduler Range (Help=true)")]
    public int Dispatch_JobScheduler_Range_Help()
    {
        for (int r = 0; r < DispatchRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobDispatchRange);
            _schedulerHelp.Complete(h);
        }
        return _dstInt[0];
    }

    #endregion

    #region 2. Throughput (Lightweight & Medium, N=10000)

    /// <summary>Serial lightweight arithmetic loop.</summary>
    [Benchmark(Description = "2a. Light N=10000 - Serial")]
    public int Throughput_Light_Serial()
    {
        for (int r = 0; r < LightRepeats; r++)
        {
            for (int i = 0; i < ThroughputN; i++)
            {
                _dstInt[i] = _srcInt[i] * 2 + 1;
            }
        }
        return _dstInt[ThroughputN - 1];
    }

    /// <summary>Parallel.For lightweight arithmetic loop.</summary>
    [Benchmark(Description = "2a. Light N=10000 - Parallel.For")]
    public int Throughput_Light_ParallelFor()
    {
        for (int r = 0; r < LightRepeats; r++)
        {
            Parallel.For(0, ThroughputN, i => _dstInt[i] = _srcInt[i] * 2 + 1);
        }
        return _dstInt[ThroughputN - 1];
    }

    /// <summary>ReusableBatchTask lightweight arithmetic loop.</summary>
    [Benchmark(Description = "2a. Light N=10000 - ReusableBatchTask")]
    public int Throughput_Light_ReusableBatch()
    {
        for (int r = 0; r < LightRepeats; r++)
        {
            _reusableLight.RunParallel(ThroughputN);
        }
        return _dstInt[ThroughputN - 1];
    }

    /// <summary>JobScheduler IBatchJob lightweight arithmetic loop.</summary>
    [Benchmark(Description = "2a. Light N=10000 - JobScheduler Batch")]
    public int Throughput_Light_JobBatch()
    {
        for (int r = 0; r < LightRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobLightBatch);
            _schedulerHelp.Complete(h);
        }
        return _dstInt[ThroughputN - 1];
    }

    /// <summary>JobScheduler IBatchRangeJob lightweight arithmetic loop.</summary>
    [Benchmark(Description = "2a. Light N=10000 - JobScheduler Range")]
    public int Throughput_Light_JobRange()
    {
        for (int r = 0; r < LightRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobLightRange);
            _schedulerHelp.Complete(h);
        }
        return _dstInt[ThroughputN - 1];
    }

    /// <summary>Serial medium math workload.</summary>
    [Benchmark(Description = "2b. Medium N=10000 - Serial")]
    public float Throughput_Medium_Serial()
    {
        for (int r = 0; r < MediumRepeats; r++)
        {
            for (int i = 0; i < ThroughputN; i++)
            {
                _dstFloat[i] = ComputeMedium(_srcFloat[i]);
            }
        }
        return _dstFloat[ThroughputN - 1];
    }

    /// <summary>Parallel.For medium math workload.</summary>
    [Benchmark(Description = "2b. Medium N=10000 - Parallel.For")]
    public float Throughput_Medium_ParallelFor()
    {
        for (int r = 0; r < MediumRepeats; r++)
        {
            Parallel.For(0, ThroughputN, i => _dstFloat[i] = ComputeMedium(_srcFloat[i]));
        }
        return _dstFloat[ThroughputN - 1];
    }

    /// <summary>ReusableBatchTask medium math workload.</summary>
    [Benchmark(Description = "2b. Medium N=10000 - ReusableBatchTask")]
    public float Throughput_Medium_ReusableBatch()
    {
        for (int r = 0; r < MediumRepeats; r++)
        {
            _reusableMedium.RunParallel(ThroughputN);
        }
        return _dstFloat[ThroughputN - 1];
    }

    /// <summary>JobScheduler IBatchJob medium math workload.</summary>
    [Benchmark(Description = "2b. Medium N=10000 - JobScheduler Batch")]
    public float Throughput_Medium_JobBatch()
    {
        for (int r = 0; r < MediumRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobMediumBatch);
            _schedulerHelp.Complete(h);
        }
        return _dstFloat[ThroughputN - 1];
    }

    /// <summary>JobScheduler IBatchRangeJob medium math workload.</summary>
    [Benchmark(Description = "2b. Medium N=10000 - JobScheduler Range")]
    public float Throughput_Medium_JobRange()
    {
        for (int r = 0; r < MediumRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobMediumRange);
            _schedulerHelp.Complete(h);
        }
        return _dstFloat[ThroughputN - 1];
    }

    #endregion

    #region 3. Skewed / Unbalanced Workload (N=2048)

    /// <summary>Static partitioning baseline via ReusableBatchTask on tail-heavy work.</summary>
    [Benchmark(Description = "3. Skewed - ReusableBatchTask (Static)")]
    public float Skewed_ReusableBatch_Static()
    {
        for (int r = 0; r < SkewedRepeats; r++)
        {
            _reusableSkewed.RunParallel(SkewedN);
        }
        return _dstFloat[SkewedN - 1];
    }

    /// <summary>Static-sized chunking on JobScheduler (batchSize = N / workers).</summary>
    [Benchmark(Description = "3. Skewed - JobScheduler StaticLike")]
    public float Skewed_JobRange_StaticLike()
    {
        int staticSize = Math.Max(1, SkewedN / Math.Max(1, _schedulerHelp.FrameWorkerCount + 1));
        for (int r = 0; r < SkewedRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobSkewedRange, batchSize: staticSize);
            _schedulerHelp.Complete(h);
        }
        return _dstFloat[SkewedN - 1];
    }

    /// <summary>Automatic dynamic chunking on JobScheduler (batchSize = 0).</summary>
    [Benchmark(Description = "3. Skewed - JobScheduler AutoDynamic")]
    public float Skewed_JobRange_AutoDynamic()
    {
        for (int r = 0; r < SkewedRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobSkewedRange, batchSize: 0);
            _schedulerHelp.Complete(h);
        }
        return _dstFloat[SkewedN - 1];
    }

    /// <summary>Fine-grained dynamic chunking on JobScheduler (batchSize = 16).</summary>
    [Benchmark(Description = "3. Skewed - JobScheduler FineDynamic (16)")]
    public float Skewed_JobRange_FineDynamic16()
    {
        for (int r = 0; r < SkewedRepeats; r++)
        {
            JobHandle h = _schedulerHelp.ScheduleBatch(_jobSkewedRange, batchSize: 16);
            _schedulerHelp.Complete(h);
        }
        return _dstFloat[SkewedN - 1];
    }

    #endregion

    #region 4. Multi-Batch Barrier Merging (3 x 256)

    /// <summary>Three separate ReusableBatchTask barriers in sequence.</summary>
    [Benchmark(Description = "4. MultiBatch - 3x ReusableBatchTask")]
    public float MultiBatch_ReusableBatch_ThreeBarriers()
    {
        for (int r = 0; r < MultiBatchRepeats; r++)
        {
            _reusableMultiA.RunParallel(MultiBatchN);
            _reusableMultiB.RunParallel(MultiBatchN);
            _reusableMultiC.RunParallel(MultiBatchN);
        }
        return _dstA[0] + _dstB[0] + _dstC[0];
    }

    /// <summary>Three parallel batches merged behind one WaitAll(JobLane.Frame) barrier.</summary>
    [Benchmark(Description = "4. MultiBatch - 3x Schedule + WaitAll(Frame)")]
    public float MultiBatch_JobScheduler_MergedWaitAll()
    {
        for (int r = 0; r < MultiBatchRepeats; r++)
        {
            _schedulerHelp.ScheduleBatch(_jobMultiA);
            _schedulerHelp.ScheduleBatch(_jobMultiB);
            _schedulerHelp.ScheduleBatch(_jobMultiC);
            _schedulerHelp.WaitAll(JobLane.Frame);
        }
        return _dstA[0] + _dstB[0] + _dstC[0];
    }

    /// <summary>Three parallel batches joined via CombineDependencies + Complete.</summary>
    [Benchmark(Description = "4. MultiBatch - 3x Schedule + Combine + Complete")]
    public float MultiBatch_JobScheduler_CombineComplete()
    {
        for (int r = 0; r < MultiBatchRepeats; r++)
        {
            JobHandle ha = _schedulerHelp.ScheduleBatch(_jobMultiA);
            JobHandle hb = _schedulerHelp.ScheduleBatch(_jobMultiB);
            JobHandle hc = _schedulerHelp.ScheduleBatch(_jobMultiC);
            JobHandle all = _schedulerHelp.CombineDependencies(ha, hb, hc);
            _schedulerHelp.Complete(all);
        }
        return _dstA[0] + _dstB[0] + _dstC[0];
    }

    #endregion

    #region 5. DAG Pipeline Overhead (5-Stage x 100)

    /// <summary>Five-stage fan-out/fan-in DAG repeated 100 times.</summary>
    [Benchmark(Description = "5. DAG Pipeline - 5-Stage x 100")]
    public int DagPipeline_5Stage_100x()
    {
        for (int r = 0; r < DagRepeats; r++)
        {
            JobHandle ha = _schedulerHelp.Schedule(_dagA);
            JobHandle hb1 = _schedulerHelp.Schedule(_dagB1, dependsOn: ha);
            JobHandle hb2 = _schedulerHelp.Schedule(_dagB2, dependsOn: ha);
            JobHandle hb3 = _schedulerHelp.Schedule(_dagB3, dependsOn: ha);
            JobHandle joinB = _schedulerHelp.CombineDependencies(hb1, hb2, hb3);
            JobHandle hc = _schedulerHelp.Schedule(_dagC, dependsOn: joinB);
            JobHandle hd1 = _schedulerHelp.Schedule(_dagD1, dependsOn: hc);
            JobHandle hd2 = _schedulerHelp.Schedule(_dagD2, dependsOn: hc);
            JobHandle joinD = _schedulerHelp.CombineDependencies(hd1, hd2);
            JobHandle he = _schedulerHelp.Schedule(_dagE, dependsOn: joinD);
            _schedulerHelp.Complete(he);
        }
        return DagRepeats;
    }

    #endregion

    #region 6. Frame Lane Isolation under Background Contention

    /// <summary>Starts two ThreadPool CPU-heavy tasks before the ThreadPool contention benchmark.</summary>
    [IterationSetup(Target = nameof(Contention_ThreadPool_Baseline))]
    public void SetupThreadPoolContention()
    {
        _stopContention = false;
        Volatile.Write(ref _startedContentionWorkers, 0);
        _threadPoolContentionTasks =
        [
            Task.Run(RunContentionLoop),
            Task.Run(RunContentionLoop),
        ];

        SpinWait spin = default;
        while (Volatile.Read(ref _startedContentionWorkers) < 2)
        {
            spin.SpinOnce();
        }
    }

    /// <summary>Stops and waits for the ThreadPool CPU-heavy tasks after the iteration.</summary>
    [IterationCleanup(Target = nameof(Contention_ThreadPool_Baseline))]
    public void CleanupThreadPoolContention()
    {
        _stopContention = true;
        if (_threadPoolContentionTasks != null)
        {
            Task.WaitAll(_threadPoolContentionTasks);
            _threadPoolContentionTasks = null;
        }
    }

    /// <summary>Starts two Background-lane CPU-heavy jobs before the Frame isolation benchmark.</summary>
    [IterationSetup(Target = nameof(Contention_JobScheduler_FrameLane))]
    public void SetupJobSchedulerContention()
    {
        _stopContention = false;
        Volatile.Write(ref _startedContentionWorkers, 0);
        _bgContentionHandle1 = _schedulerHelp.Schedule(_bgSpinJob, lane: JobLane.Background);
        _bgContentionHandle2 = _schedulerHelp.Schedule(_bgSpinJob, lane: JobLane.Background);

        SpinWait spin = default;
        while (Volatile.Read(ref _startedContentionWorkers) < 2)
        {
            spin.SpinOnce();
        }
    }

    /// <summary>Stops and waits for the Background-lane jobs after the iteration.</summary>
    [IterationCleanup(Target = nameof(Contention_JobScheduler_FrameLane))]
    public void CleanupJobSchedulerContention()
    {
        _stopContention = true;
        _schedulerHelp.Complete(_bgContentionHandle1);
        _schedulerHelp.Complete(_bgContentionHandle2);
    }

    /// <summary>ReusableBatchTask sharing ThreadPool with two heavy background Task.Run loops.</summary>
    [Benchmark(Description = "6. Contention - ReusableBatchTask vs ThreadPool")]
    public float Contention_ThreadPool_Baseline()
    {
        for (int r = 0; r < ContentionRepeats; r++)
        {
            _reusableContention.RunParallel(ContentionN);
        }
        return _dstFloat[0];
    }

    /// <summary>JobScheduler Frame lane isolated from two heavy Background-lane jobs.</summary>
    [Benchmark(Description = "6. Contention - JobScheduler Frame vs Background")]
    public float Contention_JobScheduler_FrameLane()
    {
        for (int r = 0; r < ContentionRepeats; r++)
        {
            _schedulerHelp.ScheduleBatch(_jobContention, lane: JobLane.Frame);
            _schedulerHelp.WaitAll(JobLane.Frame);
        }
        return _dstFloat[0];
    }

    private void RunContentionLoop()
    {
        Interlocked.Increment(ref _startedContentionWorkers);
        float acc = 1.0f;
        while (!_stopContention)
        {
            for (int i = 0; i < 64; i++)
            {
                acc = MathF.Sqrt(acc * MathF.Sin(acc) + MathF.Cos(acc) + 1.5f);
            }
        }
        Volatile.Write(ref _bgSink, acc);
    }

    #endregion

    private static float ComputeMedium(float x)
    {
        float value = x;
        for (int i = 0; i < 10; i++)
        {
            value = MathF.Sqrt(MathF.Abs(value * MathF.Sin(value) + MathF.Cos(value)) + 1f);
        }
        return value;
    }

    private static float ComputeSkewedItem(int index, float x)
    {
        int rounds = index >= SkewedSplit ? 60 : 2;
        float value = x;
        for (int i = 0; i < rounds; i++)
        {
            value = MathF.Sqrt(MathF.Abs(value * MathF.Sin(value) + MathF.Cos(value)) + 1f);
        }
        return value;
    }

    private sealed class EmptySingleJob : IJob
    {
        public void Execute() { }
    }

    private sealed class SingleJob(Action execute) : IJob
    {
        public void Execute() => execute();
    }

    private sealed class LightBatchJob(int count, int[] src, int[] dst) : IBatchJob
    {
        public int Count { get; } = count;
        public void Execute(int index) => dst[index] = src[index] * 2 + 1;
    }

    private sealed class LightRangeJob(int count, int[] src, int[] dst) : IBatchRangeJob
    {
        public int Count { get; } = count;

        public void Execute(int start, int count)
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                dst[i] = src[i] * 2 + 1;
            }
        }
    }

    private sealed class MediumBatchJob(int count, float[] src, float[] dst) : IBatchJob
    {
        public int Count { get; } = count;
        public void Execute(int index) => dst[index] = ComputeMedium(src[index]);
    }

    private sealed class MediumRangeJob(int count, float[] src, float[] dst) : IBatchRangeJob
    {
        public int Count { get; } = count;

        public void Execute(int start, int count)
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                dst[i] = ComputeMedium(src[i]);
            }
        }
    }

    private sealed class SkewedRangeJob(int count, float[] src, float[] dst) : IBatchRangeJob
    {
        public int Count { get; } = count;

        public void Execute(int start, int count)
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                dst[i] = ComputeSkewedItem(i, src[i]);
            }
        }
    }

    private sealed class ReusableLightTask(int[] src, int[] dst) : ReusableBatchTask
    {
        protected override void ExecuteCore(int index) => dst[index] = src[index] * 2 + 1;
    }

    private sealed class ReusableMediumTask(float[] src, float[] dst) : ReusableBatchTask
    {
        protected override void ExecuteCore(int index) => dst[index] = ComputeMedium(src[index]);
    }

    private sealed class ReusableSkewedTask(float[] src, float[] dst) : ReusableBatchTask
    {
        protected override void ExecuteCore(int index) => dst[index] = ComputeSkewedItem(index, src[index]);
    }
}
