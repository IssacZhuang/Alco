using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco.Test
{
    public class TestTransform
    {
        [Test(Description ="Box to local")]
        public void TestBoxToLocal()
        {
            ShapeBox3D box = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), math.euler(math.radians(new Vector3(0, 0, 0))));
            Transform3D transform = new Transform3D(math.euler(math.radians(new Vector3(45, 0, 0))), new Vector3(0, 0, 0));
            BoundingBox3D boxInWorld = box.TransformByParent(transform).GetBoundingBox();
            BoundingBox3D boxInLocal = box.GetBoundingBox();
            TestContext.WriteLine(boxInWorld);
            TestContext.WriteLine(boxInLocal);
        }
    }
}

