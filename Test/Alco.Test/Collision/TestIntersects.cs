using System;
using System.Numerics;
using System.Collections.Generic;

using Alco;
using System.Net.NetworkInformation;


namespace Alco.Test
{
    public class TestIntersects
    {
        [Test(Description = "test box sphere intersects")]
        public void TestIntersectsBoxSphere()
        {
            ShapeBox3D box = new ShapeBox3D(new Vector3(2, 0, 0), new Vector3(1, 1, 1), math.euler(math.radians(new Vector3(45, 45, 0))));
            ShapeSphere3D sphere = new ShapeSphere3D(new Vector3(2.3f, 0, 0), 1);
            ShapeSphere3D sphere2 = new ShapeSphere3D(new Vector3(0, 0, 0), 0.5f);
            Assert.IsFalse(!UtilsCollision3D.BoxSphere(box, sphere));
            Assert.IsFalse(UtilsCollision3D.BoxSphere(box, sphere2));
        }

        [Test(Description = "test box intersects")]
        public void TestIntersectsBox()
        {
            ShapeBox3D boxA = new ShapeBox3D(Vector3.Zero, new Vector3(0.99f), Quaternion.Identity);
            ShapeBox3D boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(0.99f), Quaternion.Identity);

            Assert.IsFalse(UtilsCollision3D.BoxBox(boxA, boxB));

            Quaternion rotX90 = math.euler(math.radians(new Vector3(90, 0, 0)));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(1.01f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(1.01f), Quaternion.Identity);

            Assert.IsTrue(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(0.99f), math.euler(math.radians(new Vector3(45, 0, 0))));
            boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(0.99f), Quaternion.Identity);

            Assert.IsFalse(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(0.99f), math.euler(math.radians(new Vector3(0, 45, 0))));
            boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(0.99f), Quaternion.Identity);

            Assert.IsTrue(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 0.2f, 0f), new Vector3(1f), math.euler(math.radians(new Vector3(45))));
            boxB = new ShapeBox3D(new Vector3(9.1f, 0.9f, 0f), new Vector3(1f), math.euler(math.radians(new Vector3(45))));

            Assert.IsTrue(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 4, 0f), new Vector3(1f), math.euler(math.radians(new Vector3(45f))));
            boxB = new ShapeBox3D(new Vector3(9.1f, 4.9f, 0f), new Vector3(1f), math.euler(math.radians(new Vector3(45f))));

            Assert.IsFalse(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(9.1f, 5.9f, 7f), new Vector3(1f), math.euler(math.radians(new Vector3(45f))));

            Assert.IsTrue(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 0.4f, 0f), new Vector3(1f), math.euler(math.radians(new Vector3(45))));
            boxB = new ShapeBox3D(new Vector3(9.1f, 0.9f, 0f), new Vector3(1f), Quaternion.Identity);

            Assert.IsTrue(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(1f), math.euler(math.radians(new Vector3(45f))));
            boxB = new ShapeBox3D(new Vector3(5f, 5f, 0f), new Vector3(1f), math.euler(math.radians(new Vector3(45f))));

            Assert.IsFalse(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(9.1f, 5, 7), new Vector3(1f), Quaternion.Identity);

            Assert.IsFalse(UtilsCollision3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(9.1f, 5, 7), new Vector3(1f), math.euler(math.radians(new Vector3(1f))));

            Assert.IsFalse(UtilsCollision3D.BoxBox(boxA, boxB));

        }

        [Test(Description = "test ray box intersects")]
        public void TestIntersectsRayBox()
        {
            ShapeBox3D box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 0, 0), new Vector3(2, 0, 0));

            Assert.IsFalse(!UtilsCollision3D.RayBox(ray, box, out RaycastHit3D hit));
            TestContext.WriteLine(hit.ToString());

            box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(2, 1.1f, 0));

            Assert.IsFalse(UtilsCollision3D.RayBox(ray, box, out hit));
            TestContext.WriteLine(hit.ToString());

            box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-0.9f, 0, 0), new Vector3(0, 0.9f, 0));

            Assert.IsFalse(!UtilsCollision3D.RayBox(ray, box, out hit));
            TestContext.WriteLine(hit.ToString());

            box = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1f), math.euler(new Vector3(0, 0, math.radians(135f))));
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-1f, -0.7f, 0), new Vector3(3, -0.7f, 0));

            Assert.IsFalse(!UtilsCollision3D.RayBox(ray, box, out hit));
            TestContext.WriteLine(hit.ToString());
        }

        [Test(Description = "test ray sphere intersects")]
        public void TestIntersectsRaySphere()
        {
            ShapeSphere3D sphere = new ShapeSphere3D(Vector3.Zero, 1f);
            Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 0, 0), new Vector3(2, 0, 0));

            Assert.IsFalse(!UtilsCollision3D.RaySphere(ray, sphere, out RaycastHit3D hit));
            TestContext.WriteLine(hit.ToString());

            sphere = new ShapeSphere3D(Vector3.Zero, 1f);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(2, 1.1f, 0));

            Assert.IsFalse(UtilsCollision3D.RaySphere(ray, sphere, out hit));
            TestContext.WriteLine(hit.ToString());

            sphere = new ShapeSphere3D(Vector3.Zero, 1f);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2f, -0.5f, 0), new Vector3(1, 2, 0));

            Assert.IsFalse(!UtilsCollision3D.RaySphere(ray, sphere, out hit));
            TestContext.WriteLine(hit.ToString());
        }

        [Test(Description = "test point box")]
        public void TestIntersectsPointBox()
        {
            ShapeBox3D box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            Vector3 point = new Vector3(0.4f, 0.4f, 0);

            Assert.IsTrue(UtilsCollision3D.PointBox(point, box));

            box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), math.euler(math.radians(new Vector3(0, 0, 45))));
            TestContext.WriteLine(box.GetBoundingBox());
            float pos = math.sqrt(2f)*0.5f*0.5f;
            TestContext.WriteLine(pos);
            point = new Vector3(pos + 0.1f, pos + 0.1f, 0);

            Assert.IsFalse(UtilsCollision3D.PointBox(point, box));

            point = new Vector3(pos - 0.1f, pos - 0.1f, 0);

            Assert.IsTrue(UtilsCollision3D.PointBox(point, box));
        }
    }
}

