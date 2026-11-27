using System;
using System.Numerics;
using System.Collections.Generic;

using Alco;
using System.Net.NetworkInformation;


namespace Alco.Test
{
    /// <summary>Tests 3D collision intersection routines.</summary>
    public class TestIntersects
    {
        /// <summary>Tests box-sphere intersection for overlapping and separated shapes.</summary>
        [Test(Description = "test box sphere intersects")]
        public void TestIntersectsBoxSphere()
        {
            ShapeBox3D box = new ShapeBox3D(new Vector3(2, 0, 0), new Vector3(1, 1, 1), math.quaternion(new Vector3(45, 45, 0)));
            ShapeSphere3D sphere = new ShapeSphere3D(new Vector3(2.3f, 0, 0), 1);
            ShapeSphere3D sphere2 = new ShapeSphere3D(new Vector3(0, 0, 0), 0.5f);
            Assert.IsFalse(!CollisionUtility3D.BoxSphere(box, sphere));
            Assert.IsFalse(CollisionUtility3D.BoxSphere(box, sphere2));
        }

        /// <summary>Tests box-box intersection for a range of positions and rotations.</summary>
        [Test(Description = "test box intersects")]
        public void TestIntersectsBox()
        {
            ShapeBox3D boxA = new ShapeBox3D(Vector3.Zero, new Vector3(0.99f), Quaternion.Identity);
            ShapeBox3D boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(0.99f), Quaternion.Identity);

            Assert.IsFalse(CollisionUtility3D.BoxBox(boxA, boxB));

            Quaternion rotX90 = math.quaternion(new Vector3(90, 0, 0));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(1.01f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(1.01f), Quaternion.Identity);

            Assert.IsTrue(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(0.99f), math.quaternion(new Vector3(45, 0, 0)));
            boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(0.99f), Quaternion.Identity);

            Assert.IsFalse(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(0.99f), math.quaternion(new Vector3(0, 45, 0)));
            boxB = new ShapeBox3D(new Vector3(1, 0, 0), new Vector3(0.99f), Quaternion.Identity);

            Assert.IsTrue(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 0.2f, 0f), new Vector3(1f), math.quaternion(new Vector3(45)));
            boxB = new ShapeBox3D(new Vector3(9.1f, 0.9f, 0f), new Vector3(1f), math.quaternion(new Vector3(45)));

            Assert.IsTrue(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 4, 0f), new Vector3(1f), math.quaternion(new Vector3(45f)));
            boxB = new ShapeBox3D(new Vector3(9.1f, 4.9f, 0f), new Vector3(1f), math.quaternion(new Vector3(45f)));

            Assert.IsFalse(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(9.1f, 5.9f, 7f), new Vector3(1f), math.quaternion(new Vector3(45f)));

            Assert.IsTrue(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 0.4f, 0f), new Vector3(1f), math.quaternion(new Vector3(45)));
            boxB = new ShapeBox3D(new Vector3(9.1f, 0.9f, 0f), new Vector3(1f), Quaternion.Identity);

            Assert.IsTrue(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(Vector3.Zero, new Vector3(1f), math.quaternion(new Vector3(45f)));
            boxB = new ShapeBox3D(new Vector3(5f, 5f, 0f), new Vector3(1f), math.quaternion(new Vector3(45f)));

            Assert.IsFalse(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(9.1f, 5, 7), new Vector3(1f), Quaternion.Identity);

            Assert.IsFalse(CollisionUtility3D.BoxBox(boxA, boxB));

            boxA = new ShapeBox3D(new Vector3(8, 5, 7f), new Vector3(1f), Quaternion.Identity);
            boxB = new ShapeBox3D(new Vector3(9.1f, 5, 7), new Vector3(1f), math.quaternion(new Vector3(1f)));

            Assert.IsFalse(CollisionUtility3D.BoxBox(boxA, boxB));

        }

        /// <summary>Tests ray-box intersection for hits and misses.</summary>
        [Test(Description = "test ray box intersects")]
        public void TestIntersectsRayBox()
        {
            ShapeBox3D box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 0, 0), new Vector3(2, 0, 0));

            Assert.IsFalse(!CollisionUtility3D.RayBox(ray, box, out RaycastHit3D hit));
            TestContext.WriteLine(hit.ToString());

            box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(2, 1.1f, 0));

            Assert.IsFalse(CollisionUtility3D.RayBox(ray, box, out hit));
            TestContext.WriteLine(hit.ToString());

            box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-0.9f, 0, 0), new Vector3(0, 0.9f, 0));

            Assert.IsFalse(!CollisionUtility3D.RayBox(ray, box, out hit));
            TestContext.WriteLine(hit.ToString());

            box = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1f), math.quaternion(new Vector3(0, 0, 135f)));
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-1f, -0.7f, 0), new Vector3(3, -0.7f, 0));

            Assert.IsFalse(!CollisionUtility3D.RayBox(ray, box, out hit));
            TestContext.WriteLine(hit.ToString());
        }

        /// <summary>Tests ray-sphere intersection for hits and misses.</summary>
        [Test(Description = "test ray sphere intersects")]
        public void TestIntersectsRaySphere()
        {
            ShapeSphere3D sphere = new ShapeSphere3D(Vector3.Zero, 1f);
            Ray3D ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 0, 0), new Vector3(2, 0, 0));

            Assert.IsFalse(!CollisionUtility3D.RaySphere(ray, sphere, out RaycastHit3D hit));
            TestContext.WriteLine(hit.ToString());

            sphere = new ShapeSphere3D(Vector3.Zero, 1f);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2, 1.1f, 0), new Vector3(2, 1.1f, 0));

            Assert.IsFalse(CollisionUtility3D.RaySphere(ray, sphere, out hit));
            TestContext.WriteLine(hit.ToString());

            sphere = new ShapeSphere3D(Vector3.Zero, 1f);
            ray = Ray3D.CreateWithStartAndEnd(new Vector3(-2f, -0.5f, 0), new Vector3(1, 2, 0));

            Assert.IsFalse(!CollisionUtility3D.RaySphere(ray, sphere, out hit));
            TestContext.WriteLine(hit.ToString());
        }

        /// <summary>Tests point-box containment for rotated boxes.</summary>
        [Test(Description = "test point box")]
        public void TestIntersectsPointBox()
        {
            ShapeBox3D box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), Quaternion.Identity);
            Vector3 point = new Vector3(0.4f, 0.4f, 0);

            Assert.IsTrue(CollisionUtility3D.PointBox(point, box));

            box = new ShapeBox3D(Vector3.Zero, new Vector3(1f), math.quaternion(new Vector3(0, 0, 45)));
            TestContext.WriteLine(box.GetBoundingBox());
            float pos = math.sqrt(2f)*0.5f*0.5f;
            TestContext.WriteLine(pos);
            point = new Vector3(pos + 0.1f, pos + 0.1f, 0);

            Assert.IsFalse(CollisionUtility3D.PointBox(point, box));

            point = new Vector3(pos - 0.1f, pos - 0.1f, 0);

            Assert.IsTrue(CollisionUtility3D.PointBox(point, box));
        }

        /// <summary>
        /// Regression tests for float32 catastrophic cancellation in 2D and 3D RaySphere
        /// across very long rays, large world coordinates, tiny radii, inside starts, and behind-ray spheres.
        /// </summary>
        [Test(Description = "long-ray and large-coordinate RaySphere precision in 2D and 3D")]
        public void TestRaySphereNumericalPrecision2DAnd3D()
        {
            // 2D: 60000-unit ray grazing/hitting a tiny r=0.15 sphere near midpoint and at +/-50000
            float fraction2D = 1f;
            Assert.IsTrue(CollisionUtility2D.RaySphere(
                new Vector2(0f, 0f),
                new Vector2(60000f, 0f),
                new Vector2(30000f, 0.05f),
                0.15f,
                ref fraction2D,
                out Vector2 n2D));
            Assert.That(fraction2D, Is.InRange(0.49f, 0.501f));
            Assert.That(n2D.Length(), Is.EqualTo(1f).Within(1e-3f));

            // 2D narrow miss at long distance
            float missFraction2D = 1f;
            Assert.IsFalse(CollisionUtility2D.RaySphere(
                new Vector2(-50000f, -50000f),
                new Vector2(100000f, 0f),
                new Vector2(0f, -50000f + 0.25f),
                0.15f,
                ref missFraction2D,
                out _));

            // 2D inside sphere -> miss, behind ray -> miss
            float inside2D = 1f;
            Assert.IsFalse(CollisionUtility2D.RaySphere(
                new Vector2(50000f, 50000f),
                new Vector2(100f, 0f),
                new Vector2(50000.05f, 50000f),
                0.2f,
                ref inside2D,
                out _));
            Assert.IsFalse(CollisionUtility2D.RaySphere(
                new Vector2(50000f, 50000f),
                new Vector2(100f, 0f),
                new Vector2(49990f, 50000f),
                0.2f,
                ref inside2D,
                out _));

            // 3D: 60000-unit ray hitting tiny r=0.15 sphere at +/-50000 coordinates
            float fraction3D = 1f;
            Assert.IsTrue(CollisionUtility3D.RaySphere(
                new Vector3(-50000f, 50000f, -25000f),
                new Vector3(100000f, 0f, 0f),
                new Vector3(0f, 50000f + 0.05f, -25000f),
                0.15f,
                ref fraction3D,
                out Vector3 n3D));
            Assert.That(fraction3D, Is.InRange(0.49f, 0.501f));
            Assert.That(n3D.Length(), Is.EqualTo(1f).Within(1e-3f));

            // 3D narrow miss at large coordinates
            float missFraction3D = 1f;
            Assert.IsFalse(CollisionUtility3D.RaySphere(
                new Vector3(-50000f, 50000f, -25000f),
                new Vector3(100000f, 0f, 0f),
                new Vector3(0f, 50000f + 0.25f, -25000f),
                0.15f,
                ref missFraction3D,
                out _));
        }
    }
}

