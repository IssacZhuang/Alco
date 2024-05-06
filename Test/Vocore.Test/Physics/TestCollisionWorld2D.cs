using System.Collections.Generic;
using System.Numerics;

namespace Vocore.Test;

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

        public TestSphereCaster(ShapeSphere2D shape)
        {
            this.shape = shape;
        }
        public void OnHit(object hitObject)
        {

            if (hitObject is TestBoxTarget target)
            {
                hitIds.Add(target.id);
            }
        }
    }

    [Test(Description = "collision world 3d")]
    public void Test_CollisionWorld2D()
    {
        using CollisionWorld2D world = new CollisionWorld2D();

        TestSphereCaster caster1 = new TestSphereCaster(new ShapeSphere2D
        {
            center = new Vector2(10, 0),
            radius = 10.1f
        });

        TestSphereCaster caster2 = new TestSphereCaster(new ShapeSphere2D
        {
            center = new Vector2(70, 0),
            radius = 10.1f
        });

        world.PushCaster(caster1, caster1.shape);
        world.PushCaster(caster2, caster2.shape);

        int boxCount = 100;
        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox2D(new Vector2(i, 0), new Vector2(1, 1), Rotation2D.Identity)
            };
            world.PushTarget(target, target.shape);
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

        world.PushCaster(caster1, caster1.shape);
        world.PushCaster(caster2, caster2.shape);

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
            world.PushTarget(target, target.shape);
            //TestContext.WriteLine($"{i}, {target.shape}, {UtilsCollision2D.BoxSphere(target.shape, caster1.shape)}");
        };

        world.PushCaster(caster1, caster1.shape);
        world.PushCaster(caster2, caster2.shape);

        world.BuildTree();
        world.Simulate();
        Assert.That(caster1.hitIds.Count, Is.EqualTo(21));
        Assert.That(caster2.hitIds.Count, Is.EqualTo(21));

    }
}