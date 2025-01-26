using System;
using System.Collections.Generic;



namespace Alco
{
    public interface IShape3D
    {
        BoundingBox3D GetBoundingBox();
    }
}

