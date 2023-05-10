using System;
using System.Collections.Generic;

using Vocore;
using Unity.Mathematics;

namespace Vocore.Test
{
    public class TestIntersects
    {
        [Test("test box sphere intersects")]
        public void TestIntersectsBoxSphere()
        {
            ShapeBox box = new ShapeBox(new float3(2, 0, 0), new float3(1, 1, 1), quaternion.EulerXYZ(45, 45, 0));
            ShapeSphere sphere = new ShapeSphere(new float3(2.3f, 0, 0), 1);
            ShapeSphere sphere2 = new ShapeSphere(new float3(0, 0, 0), 0.5f);
            TestHelper.Assert(!UtilsCollision.BoxSphere(box, sphere));
            TestHelper.Assert(UtilsCollision.BoxSphere(box, sphere2));
        }

        [Test("test box intersects")]
        public void TestIntersectsBox()
        {
            ShapeBox boxA = new ShapeBox(float3.zero, new float3(0.5f), quaternion.identity);
            ShapeBox boxB = new ShapeBox(new float3(2f, 2f, 2f), new float3(0.5f), quaternion.identity);

            TestHelper.Assert(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(float3.zero, new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));
            boxB = new ShapeBox(new float3(0.9f, 0.9f, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));

            TestHelper.Assert(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(float3.zero, new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));
            boxB = new ShapeBox(new float3(5f, 5f, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));

            TestHelper.Assert(UtilsCollision.BoxBox(boxA, boxB));

        }
    }
}

