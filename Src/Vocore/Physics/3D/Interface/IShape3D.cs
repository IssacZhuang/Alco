using System;
using System.Collections.Generic;



namespace Vocore
{
    public interface IShape3D
    {
        BoundingBox3D GetBoundingBox();
        BoundingBox3D GetBoundingBox(Transform transform);
    }
}

