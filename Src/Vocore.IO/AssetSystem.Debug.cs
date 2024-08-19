namespace Vocore.IO;

public sealed partial class AssetSystem
{
    // for unit test only

    //return finished job count
    internal int WaitForAllJobComplete()
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

}