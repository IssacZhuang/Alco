using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// Represents a parallel loop body invoked for the given index.
    /// </summary>
    /// <param name="index">The iteration index.</param>
    public delegate void ParallelForDelegate(int index);
    /// <summary>
    /// Represents a parameterless job callback.
    /// </summary>
    public delegate void JobDelegate();
}

