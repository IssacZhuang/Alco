using System;
using System.Collections.Generic;



namespace Vocore
{
    public interface IShape
    {
        BoundingBox GetBoundingBox();
        BoundingBox GetBoundingBox(Tranform transform);
    }
}

