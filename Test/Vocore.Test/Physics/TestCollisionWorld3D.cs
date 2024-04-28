namespace Vocore.Test;

public class TestCollisionWorld3D
{
    public class TestBoxTarget
    {
        public int id;
    }

    public class TestSphereCaster : ICollisionCaster
    {
        void OnHit(object other)
        {
            
        }
    }

    [Test(Description = "collision world 3d")]
    public void Test_CollisionWorld3D()
    {
        using CollisionWorld3D world = new CollisionWorld3D();

    }
}