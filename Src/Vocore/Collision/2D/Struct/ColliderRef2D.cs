using System;
using System.Collections.Generic;


namespace Vocore
{
    public unsafe struct ColliderRef2D : ICollider2D
    {
        private void* _ptr;

        private ColliderType2D _type;
        public int userData;

        public bool HasCollider => _ptr != null;

        public static ColliderRef2D Create<T>(T* collider) where T : unmanaged, ICollider2D
        {

            return new ColliderRef2D
            {
                _ptr = collider,
                _type = (*collider).Type
            };
        }

        public ColliderType2D Type => _type;

        public bool CollidesWith<T>(T other) where T : unmanaged, ICollider2D
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).CollidesWith(other);
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).CollidesWith(other);
            }
            return false;
        }

        public BoundingBox2D GetBoundingBox()
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).GetBoundingBox();
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).GetBoundingBox();
            }
            return new BoundingBox2D();
        }

        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).IntersectRay(ray, out hitInfo);
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).IntersectRay(ray, out hitInfo);
            }
            hitInfo = new RaycastHit2D();
            return false;
        }
    }
}

