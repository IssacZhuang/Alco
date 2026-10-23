using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// Exposes a numeric sort key used for ordering.
    /// </summary>
    public interface ISortable
    {
        float SortKey { get; }
    }
}

