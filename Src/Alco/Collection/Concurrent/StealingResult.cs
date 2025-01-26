using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// The stealing result of WorkStealingDeque. Used by <see cref="CircularWorkStealingDeque{T}"/> and <see cref="IndexWorkStealingDeque"/>.
    /// </summary>
    public enum StealingResult
    {
        /// <summary>
        /// There is item in the queue and it is successfully stolen.
        /// </summary>
        Success,
        /// <summary>
        /// There is no item in the queue.
        /// </summary>
        Empty,
        /// <summary>
        /// There is item in the queue but the item cannot be stolen because of the CAS failure.<br/>
        /// This is happened when the more than one thread try to steal the same item at the same time.
        /// This is not an error, just try steal again.
        /// </summary>
        CASFailed
    }
}

