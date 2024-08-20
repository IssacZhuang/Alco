namespace Vocore.IO;

public sealed partial class AssetSystem
{
    // for unit test only

    //return finished job count
    internal int DebugWaitForAllJobComplete()
    {
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