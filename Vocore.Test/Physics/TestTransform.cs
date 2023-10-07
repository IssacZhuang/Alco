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
    }
}

