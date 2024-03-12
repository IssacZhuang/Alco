using System;
using System.Collections.Generic;

namespace Vocore
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
        /// There is item in the queue but the stealing is interrupted by other thread.
        /// </summary>
        Interrupted
    }
}

