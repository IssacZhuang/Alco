using System;
using System.Collections.Generic;



namespace Vocore.Test
{
    public class TestTransform
    {
        [Test("test transform")]
        public void TestTransformToLocal()
        {
            RigidTransform parent = new RigidTransform(quaternion.AxisAngle(new float3(0, 0, 1), 1), new float3(0, 0, 0));
            RigidTransform child = new RigidTransform(quaternion.identity, new float3(1, 0, 0));

            RigidTransform local = UtilsTranform.ToLocal(child, parent);
            UnitTest.PrintBlue(UtilsTranform.Angle(parent.rot, child.rot));
            UnitTest.PrintBlue(local.pos);
        }
    }
}

