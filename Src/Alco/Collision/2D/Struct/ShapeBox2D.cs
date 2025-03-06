using System;
using System.Numerics;

namespace Alco
{
    public struct ShapeBox2D : IShape2D
    {
        public Vector2 Center;
        public Vector2 Extends;
        public Rotation2D Rotation;

        public ShapeBox2D(Vector2 center, Vector2 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = Rotation2D.Identity;
        }

        public ShapeBox2D(Vector2 center, Rotation2D rotation, Vector2 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }

        public ShapeBox2D(Vector2 center, Vector2 size, Rotation2D rotation)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }

        public BoundingBox2D GetBoundingBox()
        {
            if (Rotation == Rotation2D.Identity)
            {
                return new BoundingBox2D(Center - Extends, Center + Extends);
            }

            Vector2 x = math.rotate(Rotation, new Vector2(Extends.X, 0));
            Vector2 y = math.rotate(Rotation, new Vector2(0, Extends.Y));

            Vector2 extendsInBoudingBox = math.abs(x) + math.abs(y);
            //Vector2 extendsInBoudingBox = math.abs(math.rotate(extends, rotation));
            return new BoundingBox2D(Center - extendsInBoudingBox, Center + extendsInBoudingBox);
        }

        public ShapeBox2D TransformByParent(Transform2D parent)
        {
            return new ShapeBox2D
            {
                Center = math.rotate(Center, parent.Rotation) * parent.Scale + parent.Position,
                Extends = Extends * parent.Scale,
                Rotation = math.mul(Rotation, parent.Rotation)
            };
        }
    }
}