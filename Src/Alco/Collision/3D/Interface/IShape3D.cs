using System;
using System.Collections.Generic;



namespace Alco
{
    /// <summary>
    /// A 3D shape that can report its axis-aligned bounding box.
    /// </summary>
    public interface IShape3D
    {
        BoundingBox3D GetBoundingBox();
    }
}

