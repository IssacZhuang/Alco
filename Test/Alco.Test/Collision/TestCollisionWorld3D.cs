using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test;

public class TestCollisionWorld3D
{
    public class TestBoxTarget
    {
        public int id;
        public ShapeBox3D shape;
    }

    public class TestSphereCaster : ICollisionCaster
    {
        public List<int> hitIds = new List<int>();
        public ShapeSphere3D shape;
        public int cutomData;

        public TestSphereCaster(ShapeSphere3D shape)
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

    public class TestRayCaster3D : IRayCaster3D
    {
        public int? hitId;
        public int expectedUserData;
        public RaycastHit3D? hitInfo;

        public TestRayCaster3D(int expectedUserData)
        {
            this.expectedUserData = expectedUserData;
        }

        public void OnHit(object hitObject, in RaycastHit3D hit, int userData)
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
    public void TestShapeCast3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

        TestSphereCaster caster1 = new TestSphereCaster(new ShapeSphere3D
        {
            Center = new Vector3(10, 0, 0),
            Radius = 10.1f
        });

        TestSphereCaster caster2 = new TestSphereCaster(new ShapeSphere3D
        {
            Center = new Vector3(70, 0, 0),
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
                shape = new ShapeBox3D(new Vector3(i, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
            //TestContext.WriteLine($"{i}, {target.shape}, {UtilsCollision3D.BoxSphere(target.shape, caster1.shape)}");
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
                shape = new ShapeBox3D(new Vector3(i, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
            };
            world.PushCollisionTarget(target, target.shape);
            //TestContext.WriteLine($"{i}, {target.shape}, {UtilsCollision3D.BoxSphere(target.shape, caster1.shape)}");
        };

        world.PushCollisionCaster(caster1, caster1.shape, caster1.cutomData);
        world.PushCollisionCaster(caster2, caster2.shape, caster2.cutomData);

        world.BuildTree();
        world.Simulate();
        Assert.That(caster1.hitIds.Count, Is.EqualTo(21));
        Assert.That(caster2.hitIds.Count, Is.EqualTo(21));

    }

    [Test]
    public void TestPointCast3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

        Vector3 point = new Vector3(0, 0, 0);
        // the shape is not in use
        TestSphereCaster caster1 = new TestSphereCaster(new ShapeSphere3D
        {
            Center = new Vector3(10, 0, 0),
            Radius = 10.1f
        });

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

        world.CastPoint(caster1, point, caster1.cutomData);
        Assert.That(caster1.hitIds.Count, Is.EqualTo(3));
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

        // Ray from left to right along x-axis at y=0,z=0; should hit the box with id 0 first
        int userData1 = 456;
        TestRayCaster3D rayCaster1 = new TestRayCaster3D(userData1);
        Ray3D ray1 = new Ray3D(new Vector3(-10, 0, 0), new Vector3(100, 0, 0));
        world.PushRayCaster(rayCaster1, ray1, userData1);

        // Ray above boxes at y=5, no hit expected
        int userData2 = 789;
        TestRayCaster3D rayCaster2 = new TestRayCaster3D(userData2);
        Ray3D ray2 = new Ray3D(new Vector3(-10, 5, 0), new Vector3(100, 0, 0));
        world.PushRayCaster(rayCaster2, ray2, userData2);

        world.Simulate();

        Assert.That(rayCaster1.hitId.HasValue, Is.True);
        Assert.That(rayCaster1.hitId.Value, Is.EqualTo(42));
        Assert.That(rayCaster2.hitId.HasValue, Is.False);
    }
}