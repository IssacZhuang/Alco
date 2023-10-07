using System;
using System.Numerics;
using System.Collections.Generic;

using Vocore;


namespace Vocore.Test
{
    public class TestIntersects
    {
        [Test("test box sphere intersects")]
        public void TestIntersectsBoxSphere()
        {
            ShapeBox box = new ShapeBox(new Vector3(2, 0, 0), new Vector3(1, 1, 1), math.EulerXYZ(45, 45, 0));
            ShapeSphere sphere = new ShapeSphere(new Vector3(2.3f, 0, 0), 1);
            ShapeSphere sphere2 = new ShapeSphere(new Vector3(0, 0, 0), 0.5f);
            UnitTest.AssertFalse(!UtilsCollision.BoxSphere(box, sphere));
            UnitTest.AssertFalse(UtilsCollision.BoxSphere(box, sphere2));
        }

        [Test("test box intersects")]
        public void TestIntersectsBox()
        {
            ShapeBox boxA = new ShapeBox(Vector3.Zero, new Vector3(0.5f), Quaternion.Identity);
            ShapeBox boxB = new ShapeBox(new Vector3(2f, 2f, 2f), new Vector3(0.5f), Quaternion.Identity);

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new Vector3(8, 0, 0f), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));
            boxB = new ShapeBox(new Vector3(9.1f, 0.9f, 0f), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));

            UnitTest.AssertFalse(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new Vector3(8, 4, 0f), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));
            boxB = new ShapeBox(new Vector3(9.1f, 4.9f, 0f), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));

            UnitTest.AssertFalse(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox(new Vector3(9.1f, 5.9f, 7f), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));

            UnitTest.AssertFalse(!UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(Vector3.Zero, new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));
            boxB = new ShapeBox(new Vector3(5f, 5f, 0f), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(45f))));

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox(new Vector3(9.1f, 5, 7), new Vector3(1f), Quaternion.Identity);

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

            boxA = new ShapeBox(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox(new Vector3(9.1f, 5, 7), new Vector3(1f), math.EulerXYZ(math.radians(new Vector3(1f))));

            UnitTest.AssertFalse(UtilsCollision.BoxBox(boxA, boxB));

        }

        [Test("test ray box intersects")]
        public void TestIntersectsRayBox()
        {
            ShapeBox box = new ShapeBox(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            Ray ray = Ray.CreateWithStartAndEnd(new Vector3(-2, 0, 0), new Vector3(2, 0, 0));

            UnitTest.AssertFalse(!UtilsCollision.RayBox(ray, box, out RaycastHit hit));
            UnitTest.PrintBlue(hit.ToString());

            box = new ShapeBox(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            ray = Ray.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(2, 1.1f, 0));

            UnitTest.AssertFalse(UtilsCollision.RayBox(ray, box, out hit));
            UnitTest.PrintBlue(hit.ToString());

            box = new ShapeBox(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            ray = Ray.CreateWithStartAndEnd(new Vector3(-0.9f, 0, 0), new Vector3(0, 0.9f, 0));

            UnitTest.AssertFalse(!UtilsCollision.RayBox(ray, box, out hit));
            UnitTest.PrintBlue(hit.ToString());

            box = new ShapeBox(new Vector3(0, 0, 0), new Vector3(1f), math.EulerXYZ(new Vector3(0, 0, math.radians(135f))));
            ray = Ray.CreateWithStartAndEnd(new Vector3(-1f, -0.7f, 0), new Vector3(3, -0.7f, 0));

            UnitTest.AssertFalse(!UtilsCollision.RayBox(ray, box, out hit));
            UnitTest.PrintBlue(hit.ToString());
        }

        [Test("test ray sphere intersects")]
        public void TestIntersectsRaySphere()
        {
            ShapeSphere sphere = new ShapeSphere(Vector3.Zero, 1f);
            Ray ray = Ray.CreateWithStartAndEnd(new Vector3(-2, 0, 0), new Vector3(2, 0, 0));

            UnitTest.AssertFalse(!UtilsCollision.RaySphere(ray, sphere, out RaycastHit hit));
            UnitTest.PrintBlue(hit.ToString());

            sphere = new ShapeSphere(Vector3.Zero, 1f);
            ray = Ray.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(2, 1.1f, 0));

            UnitTest.AssertFalse(UtilsCollision.RaySphere(ray, sphere, out hit));
            UnitTest.PrintBlue(hit.ToString());

            sphere = new ShapeSphere(Vector3.Zero, 1f);
            ray = Ray.CreateWithStartAndEnd(new Vector3(-2f, -0.5f, 0), new Vector3(1, 2, 0));

            UnitTest.AssertFalse(!UtilsCollision.RaySphere(ray, sphere, out hit));
            UnitTest.PrintBlue(hit.ToString());
        }
    }
}

