using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test;

public unsafe class TestCollisionWorld2D
{
    public class TestBoxTarget
    {
        public int id;
        public ShapeBox2D shape;
    }

    public struct TestSphereCollector : ICollisionCastCollector
    {
        public List<int> hitIds;
        public int cutomData;

        public TestSphereCollector(int customData)
        {
            this.hitIds = new List<int>();
            this.cutomData = customData;
        }

        public bool OnHit(object hitObject)
        {
            if (hitObject is TestBoxTarget target)
            {
                hitIds.Add(target.id);
            }
            return true;
        }
    }

    [Test(Description = "collision world 2d")]
    public void TestShapeCast2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        ShapeSphere2D shape1 = new ShapeSphere2D
        {
            Center = new Vector2(10, 0),
            Radius = 10.1f
        };
        TestSphereCollector collector1 = new TestSphereCollector(123);

        ShapeSphere2D shape2 = new ShapeSphere2D
        {
            Center = new Vector2(70, 0),
            Radius = 10.1f
        };
        TestSphereCollector collector2 = new TestSphereCollector(123);

        int boxCount = 100;
        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox2D(new Vector2(i, 0), new Vector2(1, 1), Rotation2D.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
        };

        world.BuildTree();

        world.CastSphere(ref collector1, shape1);
        world.CastSphere(ref collector2, shape2);

        Assert.That(collector1.hitIds.Count, Is.EqualTo(21));
        Assert.That(collector2.hitIds.Count, Is.EqualTo(21));


        //reuse test: no target

        collector1.hitIds.Clear();
        collector2.hitIds.Clear();
        world.ClearAll();

        world.BuildTree();

        world.CastSphere(ref collector1, shape1);
        world.CastSphere(ref collector2, shape2);

        Assert.That(collector1.hitIds.Count, Is.EqualTo(0));
        Assert.That(collector2.hitIds.Count, Is.EqualTo(0));


        // reuse test with target

        collector1.hitIds.Clear();
        collector2.hitIds.Clear();
        world.ClearAll();

        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox2D(new Vector2(i, 0), new Vector2(1, 1), Rotation2D.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
        };

        world.BuildTree();

        world.CastSphere(ref collector1, shape1);
        world.CastSphere(ref collector2, shape2);

        Assert.That(collector1.hitIds.Count, Is.EqualTo(21));
        Assert.That(collector2.hitIds.Count, Is.EqualTo(21));

    }

    [Test]
    public void TestPointCast2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        Vector2 point = new Vector2(0, 0);
        TestSphereCollector collector1 = new TestSphereCollector(123);

        //hit 
        TestBoxTarget box1 = new TestBoxTarget
        {
            id = 0,
            shape = new ShapeBox2D(new Vector2(0, 0), new Vector2(1, 1), Rotation2D.Identity)
        };

        //hit
        TestBoxTarget box2 = new TestBoxTarget
        {
            id = 1,
            shape = new ShapeBox2D(new Vector2(0.2f, 0), new Vector2(1, 1), Rotation2D.Identity)
        };

        //hit
        TestBoxTarget box3 = new TestBoxTarget
        {
            id = 2,
            shape = new ShapeBox2D(new Vector2(0.4f, 0), new Vector2(1, 1), Rotation2D.Identity)
        };

        //not hit
        TestBoxTarget box4 = new TestBoxTarget
        {
            id = 3,
            shape = new ShapeBox2D(new Vector2(0.6f, 0), new Vector2(1, 1), Rotation2D.Identity)
        };

        world.PushCollisionTarget(box1, box1.shape);
        world.PushCollisionTarget(box2, box2.shape);
        world.PushCollisionTarget(box3, box3.shape);
        world.PushCollisionTarget(box4, box4.shape);

        world.BuildTree();

        world.CastPoint(ref collector1, point);
        Assert.That(collector1.hitIds.Count, Is.EqualTo(3));
    }

    [Test]
    public void TestRayCast2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        // Single hit target to avoid dependency on BVH traversal order
        TestBoxTarget targetHit = new TestBoxTarget
        {
            id = 42,
            shape = new ShapeBox2D(new Vector2(0, 0), new Vector2(1, 1), Rotation2D.Identity)
        };
        world.PushCollisionTarget(targetHit, targetHit.shape);

        world.BuildTree();

        // Ray from left to right along x-axis at y=0; should hit the box with id 0 first
        Ray2D ray1 = new Ray2D(new Vector2(-10, 0), new Vector2(100, 0));
        bool hit1 = world.TryCastRayClosestHit<TestBoxTarget>(ray1, out TestBoxTarget hitObject1, out RaycastHit2D hitInfo1);

        // Ray above boxes at y=5, no hit expected
        Ray2D ray2 = new Ray2D(new Vector2(-10, 5), new Vector2(100, 0));
        bool hit2 = world.TryCastRayClosestHit<TestBoxTarget>(ray2, out TestBoxTarget hitObject2, out RaycastHit2D hitInfo2);

        Assert.That(hit1, Is.True);
        Assert.That(hitObject1, Is.Not.Null);
        Assert.That(hitObject1!.id, Is.EqualTo(42));

        Assert.That(hit2, Is.False);
        Assert.That(hitObject2, Is.Null);
    }

    [Test]
    public void TestRayCastMulti2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        for (int i = 0; i < 5; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox2D(new Vector2(i * 10, 0), new Vector2(1, 1), Rotation2D.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
        }

        world.BuildTree();

        // Ray passing through all 5 boxes
        Ray2D ray = new Ray2D(new Vector2(-5, 0), new Vector2(100, 0));
        List<TestBoxTarget> hits = new List<TestBoxTarget>();
        world.CastRay(hits, ray);

        Assert.That(hits.Count, Is.EqualTo(5));
    }
}