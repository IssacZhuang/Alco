using System.Collections.Generic;
using System.Numerics;

namespace Vocore.Test;

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

        public TestSphereCaster(ShapeSphere3D shape)
        {
            this.shape = shape;
        }
        public void OnHit(IReadOnlyList<object> hitObjects)
        {
            TestContext.WriteLine("hit count: " + hitObjects.Count);
            foreach (var hitObject in hitObjects)
            {
                if (hitObject is TestBoxTarget target)
                {
                    hitIds.Add(target.id);
                }
            }
        }
    }

    [Test(Description = "collision world 3d")]
    public void Test_CollisionWorld3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();
        int boxCount = 100;
        for (int i = 0; i < boxCount; i++)
        {
            TestBoxTarget target = new TestBoxTarget
            {
                id = i,
                shape = new ShapeBox3D(new Vector3(i, 0, 0), new Vector3(1, 1, 1), Quaternion.Identity)
            };
            world.AddTarget(target, target.shape);
        }

        TestSphereCaster caster1 = new TestSphereCaster(new ShapeSphere3D
        {
            center = new Vector3(10, 0, 0),
            radius = 10.1f
        });

        TestSphereCaster caster2 = new TestSphereCaster(new ShapeSphere3D
        {
            center = new Vector3(70, 0, 0),
            radius = 10.1f
        }
        );

        world.AddCaster(caster1, caster1.shape);
        world.AddCaster(caster2, caster1.shape);

        world.BuildTree();
        world.Simulate();
        Assert.That(caster1.hitIds.Count, Is.EqualTo(20));
        Assert.That(caster2.hitIds.Count, Is.EqualTo(20));

    }
}