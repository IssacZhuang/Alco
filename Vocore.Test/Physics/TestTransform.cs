using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore.Test
{
    public class TestTransform
    {
        [Test("test transform")]
        public void TestTransformToLocal()
        {
            RigidTransform parent = new RigidTransform(quaternion.Euler(0,0,45), new float3(2, 2, 0));
            RigidTransform child = new RigidTransform(quaternion.Euler(0,0,90), new float3(4, 4, 0));

            TestHelper.PrintBlue(UtilsTranform.ToLocal(child, parent));
        }
    }
}

