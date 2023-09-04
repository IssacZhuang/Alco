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
            UnitTest.AssertFalse(!UtilsCollision.BoxSphere(box, sphere));
            UnitTest.AssertFalse(UtilsCollision.BoxSphere(box, sphere2));
        }

        [Test("test box intersects")]
        public void TestIntersectsBox()
        {
            ShapeBox boxA = new ShapeBox(float3.zero, new float3(0.5f), quaternion.identity);
            ShapeBox boxB = new ShapeBox(new float3(2f, 2f, 2f), new float3(0.5f), quaternion.identity);

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new float3(8, 0, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));
            boxB = new ShapeBox(new float3(9.1f, 0.9f, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));

            UnitTest.AssertFalse(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new float3(8, 4, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));
            boxB = new ShapeBox(new float3(9.1f, 4.9f, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));

            UnitTest.AssertFalse(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new float3(8, 5, 7f), new float3(1f), quaternion.identity);
            boxB = new ShapeBox(new float3(9.1f, 5.9f, 7f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));

            UnitTest.AssertFalse(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(float3.zero, new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));
            boxB = new ShapeBox(new float3(5f, 5f, 0f), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(45f))));

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new float3(8, 5, 7f), new float3(1f), quaternion.identity);
            boxB = new ShapeBox(new float3(9.1f, 5, 7), new float3(1f), quaternion.identity);

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new float3(8, 5, 7f), new float3(1f), quaternion.identity);
            boxB = new ShapeBox(new float3(9.1f, 5, 7), new float3(1f), quaternion.EulerXYZ(math.radians(new float3(1f))));

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

        }

        [Test("test ray box intersects")]
        public void TestIntersectsRayBox()
        {
            ShapeBox box = new ShapeBox(float3.zero, new float3(1f), quaternion.identity);
            Ray ray = Ray.CreateWithStartAndEnd(new float3(-2, 0, 0), new float3(2, 0, 0));

            UnitTest.AssertFalse(!UtilsCollision.RayBox(ray, box, out RaycastHit hit));
            UnitTest.PrintBlue(hit.ToString());

            box = new ShapeBox(float3.zero, new float3(1f), quaternion.identity);
            ray = Ray.CreateWithStartAndEnd(new float3(-2, 1.1f, 0), new float3(2, 1.1f, 0));

            UnitTest.AssertFalse(UtilsCollision.RayBox(ray, box, out hit));
            UnitTest.PrintBlue(hit.ToString());

            box = new ShapeBox(float3.zero, new float3(1f), quaternion.identity);
            ray = Ray.CreateWithStartAndEnd(new float3(-0.9f, 0, 0), new float3(0, 0.9f, 0));

            UnitTest.AssertFalse(!UtilsCollision.RayBox(ray, box, out hit));
            UnitTest.PrintBlue(hit.ToString());

            box = new ShapeBox(new float3(0, 0, 0), new float3(1f), quaternion.EulerXYZ(new float3(0, 0, math.radians(135))));
            ray = Ray.CreateWithStartAndEnd(new float3(-1f, -0.7f, 0), new float3(3, -0.7f, 0));

            UnitTest.AssertFalse(!UtilsCollision.RayBox(ray, box, out hit));
            UnitTest.PrintBlue(hit.ToString());
        }

        [Test("test ray sphere intersects")]
        public void TestIntersectsRaySphere()
        {
            ShapeSphere sphere = new ShapeSphere(float3.zero, 1f);
            Ray ray = Ray.CreateWithStartAndEnd(new float3(-2, 0, 0), new float3(2, 0, 0));

            UnitTest.AssertFalse(!UtilsCollision.RaySphere(ray, sphere, out RaycastHit hit));
            UnitTest.PrintBlue(hit.ToString());

            sphere = new ShapeSphere(float3.zero, 1f);
            ray = Ray.CreateWithStartAndEnd(new float3(-2, 1.1f, 0), new float3(2, 1.1f, 0));

            UnitTest.AssertFalse(UtilsCollision.RaySphere(ray, sphere, out hit));
            UnitTest.PrintBlue(hit.ToString());

            sphere = new ShapeSphere(float3.zero, 1f);
            ray = Ray.CreateWithStartAndEnd(new float3(-2f, -0.5f, 0), new float3(1, 2, 0));

            UnitTest.AssertFalse(!UtilsCollision.RaySphere(ray, sphere, out hit));
            UnitTest.PrintBlue(hit.ToString());
        }
    }
}

