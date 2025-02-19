using System.Runtime.CompilerServices;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    private class AssetProfiler : Profiler, IDisposable
    {
        private readonly SpanStringBuilder _builder = new SpanStringBuilder(128);

        public void StartProfile(string assetName, string typeName)
        {
            _builder.Clear();
            _builder.Append("Load ");
            _builder.Append(assetName);
            _builder.Append("(");
            _builder.Append(typeName);
            _builder.Append(")");
            Start(assetName);
        }

        public void EndProfile(bool print = true)
        {
            ProfilerBlock result = End();
            if (print)
            {
                _builder.Append(" ");
                _builder.Append(result.Miliseconds);
                _builder.Append("ms");
            
                Log.Print(_builder.Buffer, ConsoleColor.Green);
            }

        }

        public void Dispose()
        {
            _builder.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StartProfile(string name, Type type)
    {
        if (IsProfileEnabled)
        {
            Profiler.Value!.StartProfile(name, type.Name);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndProfile(bool print = true)
    {
        if (IsProfileEnabled)
        {
            Profiler.Value!.EndProfile(print);
        }
    }


    // for unit test only

    //return finished job count
    internal int DebugWaitForAllJobComplete()
    {
        if (_asyncLoadQueue == null)
        {
            return 0;
        }
        var list = _asyncLoadQueue.WaitForAllCompleted();

        int count = 0;
        foreach (var job in list)
        {
            HanleFinishedJob(job.job, job.exception);
            count++;
        }

        return count;
    }

    internal bool DebugIsAssetCached(string assetPath)
    {
        return _assetLookup.TryGetValue(assetPath, out AssetHandle? handle) && handle.CachedAsset != null;
    }

}