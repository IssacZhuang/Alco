using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test;

public class TestCollisionWorld2D
{
    public class TestBoxTarget
    {
        public int id;
        public ShapeBox2D shape;
    }

    public class TestSphereCaster : ICollisionCaster
    {
        public List<int> hitIds = new List<int>();
        public ShapeSphere2D shape;
        public int cutomData;

        public TestSphereCaster(ShapeSphere2D shape)
        {
            this.shape = shape;
            this.cutomData = 123;
        }
        public void OnHit(object hitObject, int userData)
        {
            Assert.That(userData, Is.EqualTo(this.cutomData));
            if (hitObject is TestBoxTarget target)
            {
                hitIds.Add(target.id);
            }
        }
    }

    public class TestRayCaster2D : IRayCaster2D
    {
        public int? hitId;
        public int expectedUserData;
        public RaycastHit2D? hitInfo;

        public TestRayCaster2D(int expectedUserData)
        {
            this.expectedUserData = expectedUserData;
        }

        public void OnHit(object hitObject, in RaycastHit2D hit, int userData)
        {
            Assert.That(userData, Is.EqualTo(this.expectedUserData));
            if (hitObject is TestBoxTarget target)
            {
                hitId = target.id;
                hitInfo = hit;
            }
        }
    }

    [Test(Description = "collision world 3d")]
    public void TestShapeCast2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        TestSphereCaster caster1 = new TestSphereCaster(new ShapeSphere2D
        {
            Center = new Vector2(10, 0),
            Radius = 10.1f
        });

        TestSphereCaster caster2 = new TestSphereCaster(new ShapeSphere2D
        {
            Center = new Vector2(70, 0),
            Radius = 10.1f
        });

        world.PushCollisionCaster(caster1, caster1.shape, caster1.cutomData);
        world.PushCollisionCaster(caster2, caster2.shape, caster2.cutomData);

        int boxCount = 100;
        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox2D(new Vector2(i, 0), new Vector2(1, 1), Rotation2D.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
            //TestContext.WriteLine($"{i}, {target.shape}, {UtilsCollision2D.BoxSphere(target.shape, caster1.shape)}");
        };

        world.BuildTree();
        world.Simulate();
        Assert.That(caster1.hitIds.Count, Is.EqualTo(21));
        Assert.That(caster2.hitIds.Count, Is.EqualTo(21));


        //reuse test: no target

        caster1.hitIds.Clear();
        caster2.hitIds.Clear();
        world.ClearAll();

        world.PushCollisionCaster(caster1, caster1.shape);
        world.PushCollisionCaster(caster2, caster2.shape);

        world.BuildTree();
        world.Simulate();
        Assert.That(caster1.hitIds.Count, Is.EqualTo(0));
        Assert.That(caster2.hitIds.Count, Is.EqualTo(0));


        // reuse test with target

        caster1.hitIds.Clear();
        caster2.hitIds.Clear();
        world.ClearAll();

        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox2D(new Vector2(i, 0), new Vector2(1, 1), Rotation2D.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
            //TestContext.WriteLine($"{i}, {target.shape}, {UtilsCollision2D.BoxSphere(target.shape, caster1.shape)}");
        };

        world.PushCollisionCaster(caster1, caster1.shape, caster1.cutomData);
        world.PushCollisionCaster(caster2, caster2.shape, caster2.cutomData);

        world.BuildTree();
        world.Simulate();
        Assert.That(caster1.hitIds.Count, Is.EqualTo(21));
        Assert.That(caster2.hitIds.Count, Is.EqualTo(21));

    }

    [Test]
    public void TestPointCast2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        Vector2 point = new Vector2(0, 0);
        // the shape is not in use
        TestSphereCaster caster1 = new TestSphereCaster(new ShapeSphere2D
        {
            Center = new Vector2(10, 0),
            Radius = 10.1f
        });

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

        world.CastPoint(caster1, point, caster1.cutomData);
        Assert.That(caster1.hitIds.Count, Is.EqualTo(3));
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
        int userData1 = 456;
        TestRayCaster2D rayCaster1 = new TestRayCaster2D(userData1);
        Ray2D ray1 = new Ray2D(new Vector2(-10, 0), new Vector2(100, 0));
        world.PushRayCaster(rayCaster1, ray1, userData1);

        // Ray above boxes at y=5, no hit expected
        int userData2 = 789;
        TestRayCaster2D rayCaster2 = new TestRayCaster2D(userData2);
        Ray2D ray2 = new Ray2D(new Vector2(-10, 5), new Vector2(100, 0));
        world.PushRayCaster(rayCaster2, ray2, userData2);

        world.Simulate();

        Assert.That(rayCaster1.hitId.HasValue, Is.True);
        Assert.That(rayCaster1.hitId.Value, Is.EqualTo(42));
        Assert.That(rayCaster2.hitId.HasValue, Is.False);
    }
}