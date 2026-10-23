using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco.Test
{
    /// <summary>Tests transforming a box shape between local and world space.</summary>
    public class TestTransform
    {
        /// <summary>Tests box transformation by a parent transform.</summary>
        [Test(Description ="Box to local")]
        public void TestBoxToLocal()
        {
            ShapeBox3D box = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), math.quaternion(new Vector3(0, 0, 0)));
            Transform3D transform = new Transform3D(math.quaternion(new Vector3(45, 0, 0)), new Vector3(0, 0, 0));
            BoundingBox3D boxInWorld = box.TransformByParent(transform).GetBoundingBox();
            BoundingBox3D boxInLocal = box.GetBoundingBox();
            TestContext.WriteLine(boxInWorld);
            TestContext.WriteLine(boxInLocal);
        }
    }
}

