#nullable enable
using System.Collections.Generic;
using System.Numerics;
using TestFramework;

namespace Alco.Test;

public class TestCollisionWorld3D
{
    public class TestBoxTarget
    {
        public int id;
        public ShapeBox3D shape;
    }

    public struct TestCollector : ICollisionCastCollector, IRayCastCollector3D
    {
        public List<int> hitIds;
        public TestCollector() { hitIds = new List<int>(); }

        public bool OnHit(object target)
        {
            if (target is TestBoxTarget box)
            {
                hitIds.Add(box.id);
            }
            return true;
        }

        public bool OnHit(object target, RaycastHit3D hit)
        {
            if (target is TestBoxTarget box)
            {
                hitIds.Add(box.id);
            }
            return true;
        }
    }

    [Test(Description = "collision world 3d")]
    public void TestShapeCast3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

        ShapeSphere3D shape1 = new ShapeSphere3D
        {
            Center = new Vector3(10, 0, 0),
            Radius = 10.1f
        };

        ShapeSphere3D shape2 = new ShapeSphere3D
        {
            Center = new Vector3(70, 0, 0),
            Radius = 10.1f
        };

        int boxCount = 100;
        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox3D(new Vector3(i, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
        };

        world.BuildTree();

        TestCollector collector1 = new TestCollector();
        world.CastSphere(ref collector1, shape1);
        Assert.That(collector1.hitIds.Count, Is.EqualTo(21));

        TestCollector collector2 = new TestCollector();
        world.CastSphere(ref collector2, shape2);
        Assert.That(collector2.hitIds.Count, Is.EqualTo(21));


        //reuse test: no target
        world.ClearAll();
        world.BuildTree();
        collector1 = new TestCollector();
        world.CastSphere(ref collector1, shape1);
        Assert.That(collector1.hitIds.Count, Is.EqualTo(0));


        // reuse test with target
        world.ClearAll();

        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox3D(new Vector3(i, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
        };

        world.BuildTree();
        collector1 = new TestCollector();
        world.CastSphere(ref collector1, shape1);
        collector2 = new TestCollector();
        world.CastSphere(ref collector2, shape2);
        Assert.That(collector1.hitIds.Count, Is.EqualTo(21));
        Assert.That(collector2.hitIds.Count, Is.EqualTo(21));
    }

    [Test]
    public void TestPointCast3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

        Vector3 point = new Vector3(0, 0, 0);

        //hit 
        TestBoxTarget box1 = new TestBoxTarget
        {
            id = 0,
            shape = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
        };

        //hit
        TestBoxTarget box2 = new TestBoxTarget
        {
            id = 1,
            shape = new ShapeBox3D(new Vector3(0.2f, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
        };

        //hit
        TestBoxTarget box3 = new TestBoxTarget
        {
            id = 2,
            shape = new ShapeBox3D(new Vector3(0.4f, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
        };

        //not hit
        TestBoxTarget box4 = new TestBoxTarget
        {
            id = 3,
            shape = new ShapeBox3D(new Vector3(0.6f, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
        };

        world.PushCollisionTarget(box1, box1.shape);
        world.PushCollisionTarget(box2, box2.shape);
        world.PushCollisionTarget(box3, box3.shape);
        world.PushCollisionTarget(box4, box4.shape);

        world.BuildTree();

        TestCollector collector = new TestCollector();
        world.CastPoint(ref collector, point);
        Assert.That(collector.hitIds.Count, Is.EqualTo(3));
    }

    [Test]
    public void TestRayCast3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

        // Single hit target to avoid dependency on BVH traversal order
        TestBoxTarget targetHit = new TestBoxTarget
        {
            id = 42,
            shape = new ShapeBox3D(new Vector3(0, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
        };
        world.PushCollisionTarget(targetHit, targetHit.shape);

        world.BuildTree();

        // Ray from left to right along x-axis at y=0,z=0; should hit the box with id 42
        Ray3D ray1 = new Ray3D(new Vector3(-10, 0, 0), new Vector3(100, 0, 0));
        Assert.That(world.TryCastRayClosestHit(ray1, out TestBoxTarget? hitTarget1, out _), Is.True);
        Assert.That(hitTarget1?.id, Is.EqualTo(42));

        // Ray above boxes at y=5, no hit expected
        Ray3D ray2 = new Ray3D(new Vector3(-10, 5, 0), new Vector3(100, 0, 0));
        Assert.That(world.TryCastRayClosestHit(ray2, out TestBoxTarget? hitTarget2, out _), Is.False);
        Assert.That(hitTarget2, Is.Null);
    }

    [Test]
    public void TestRayCastMulti3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

        for (int i = 0; i < 5; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox3D(new Vector3(i * 10, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
        }

        world.BuildTree();

        // Ray passing through all 5 boxes
        Ray3D ray = new Ray3D(new Vector3(-5, 0, 0), new Vector3(100, 0, 0));
        TestCollector collector = new TestCollector();
        world.CastRay(ref collector, ray);

        Assert.That(collector.hitIds.Count, Is.EqualTo(5));
    }
}
