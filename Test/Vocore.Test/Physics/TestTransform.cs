using System;
using System.Numerics;
using System.Collections.Generic;



namespace Vocore.Test
{
    public class TestTransform
    {
        [Test("test transform")]
        public void TestTransformToLocal()
        {
            // RigidTransform parent = new RigidTransform(Matrix4x4.CreateFromAxisAngle(new Vector3(0, 0, 1), 1), new Vector3(0, 0, 0));
            // RigidTransform child = new RigidTransform(Quaternion.Identity, new Vector3(1, 0, 0));

            // RigidTransform local = UtilsTranform.ToLocal(child, parent);
            // UnitTest.PrintBlue(UtilsTranform.Angle(parent.rot, child.rot));
            // UnitTest.PrintBlue(local.pos);
        }

        [Test("test box to local")]
        public void TestBoxToLocal()
        {
            ShapeBox3D box = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), math.euler(math.radians(new Vector3(0, 0, 0))));
            Transform3D transform = new Transform3D(math.euler(math.radians(new Vector3(45, 0, 0))), new Vector3(0, 0, 0));
            BoundingBox3D boxInWorld = box.TransformByParent(transform).GetBoundingBox();
            BoundingBox3D boxInLocal = box.GetBoundingBox();
            UnitTest.PrintBlue(boxInWorld);
            UnitTest.PrintBlue(boxInLocal);
        }
    }
}

