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
}