namespace Vocore.IO;

public sealed partial class AssetSystem
{
    public bool IsOwnerThread(int threadId)
    {
        return _ownerThreadId == threadId;
    }

    // Only called from the GameEngine class
    internal void OnUpdate()
    {
        for (int i = 0; i < FetchFinishJobAttempCount; i++)
        {
            StealingResult result = _asyncLoadQueue.TryGetFinishedTask(out AsyncPreprocessJob job, out Exception? exception);
            if (result == StealingResult.Success)
            {
                if (exception != null)
                {
                    Log.Error($"Exception on loading asset '{job.name}': {exception}");
                    continue;
                }

                if (job.asset == null)
                {
                    Log.Error($"The preprocessed asset of '{job.name}' is null, the asset manager failed to load the asset");
                    continue;
                }

                try
                {
                    object? asset = job.asset;
                    if (asset == null)
                    {
                        Log.Error($"Failed to create asset: {job.name}");
                        continue;
                    }

                    AssetHandle handle = job.handle;
                    handle.SetCache(job.asset, job.cacheMode);
                    handle.DoLoadComplete(asset);
                    handle.ClearLoadComplete();
                    handle.IsLoading = false;
                }
                catch (Exception e)
                {
                    Log.Error($"Exception on creating asset '{job.name}': {e}");
                }

            }

            if (result == StealingResult.Empty)
            {
                return;
            }
        }
    }

    private void CheckThread()
    {
        if (!IsOwnerThread(Environment.CurrentManagedThreadId))
        {
            throw new Exception("The asset manager can only be accessed by the thread that created it which usually is the main thread");
        }
    }

    internal void SetMainThread()
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

}