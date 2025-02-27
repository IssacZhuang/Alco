using System;
using System.Numerics;

namespace Alco
{
    public struct ShapeSphere2D : IShape2D
    {
        public Vector2 Center;
        public float Radius;

        public ShapeSphere2D(Vector2 center, float radius)
        {
            this.Center = center;
            this.Radius = radius;
        }

        public BoundingBox2D GetBoundingBox()
        {
            return new BoundingBox2D(Center - new Vector2(Radius), Center + new Vector2(Radius));
        }

        public ShapeSphere2D TransformByParent(Transform2D parent)
        {
            return new ShapeSphere2D
            {
                Center = math.rotate(Center, parent.Rotation) * parent.Scale + parent.Position,
                Radius = Radius * math.max(parent.Scale.X, parent.Scale.Y)
            };
        }

        public override string ToString()
        {
            return $"Sphere: {Center}, {Radius}";
        }
    }
}