using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// A 2D shape that can report its axis-aligned bounding box.
    /// </summary>
    public interface IShape2D
    {
        BoundingBox2D GetBoundingBox();
    }
}

