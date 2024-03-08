using System;
using System.Numerics;

namespace Vocore
{
    public struct ShapeSphere2D : IShape2D
    {
        public Vector2 center;
        public float radius;

        public ShapeSphere2D(Vector2 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public BoundingBox2D GetBoundingBox()
        {
            return new BoundingBox2D(center - new Vector2(radius), center + new Vector2(radius));
        }

        public ShapeSphere2D TransformByParent(Transform2D parent)
        {
            return new ShapeSphere2D
            {
                center = math.rotate(center, parent.rotation) * parent.scale + parent.position,
                radius = radius * math.max(parent.scale.X, parent.scale.Y)
            };
        }
    }
}