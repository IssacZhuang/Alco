using System;

namespace Alco
{
    /// <summary>
    /// Defines a batch of indexed jobs that can be executed in parallel.
    /// </summary>
    public interface IJobBatch
    {
        void Execute(int i);
    }
}