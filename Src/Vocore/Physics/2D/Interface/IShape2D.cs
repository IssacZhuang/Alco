using System;
using System.Collections.Generic;

namespace Vocore
{
    public interface IShape2D
    {
        BoundingBox3D GetBoundingBox();
        BoundingBox3D GetBoundingBox(Transform3D transform);
    }
}

