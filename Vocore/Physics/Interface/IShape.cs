using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public interface IShape
    {
        BoundingBox GetBoundingBox();
        BoundingBox GetBoundingBox(RigidTransform transform);
    }
}

