using System;
using System.Numerics;

namespace Vocore
{
    public struct ShapeBox2D : IShape2D
    {
        public Vector2 center;
        public Vector2 extends;
        public Rotation2D rotation;
        public ShapeBox2D(Vector2 center, Vector2 size, Rotation2D rotation)
        {
            this.center = center;
            this.extends = size * 0.5f;
            this.rotation = rotation;
        }

        public BoundingBox2D GetBoundingBox()
        {
            if (rotation == Rotation2D.Identity)
            {
                return new BoundingBox2D(center - extends, center + extends);
            }

            Vector2 x = math.rotate(rotation, new Vector2(extends.X, 0));
            Vector2 y = math.rotate(rotation, new Vector2(0, extends.Y));

            Vector2 extendsInBoudingBox = math.abs(x) + math.abs(y);
            //Vector2 extendsInBoudingBox = math.abs(math.rotate(extends, rotation));
            return new BoundingBox2D(center - extendsInBoudingBox, center + extendsInBoudingBox);
        }

        public ShapeBox2D TransformByParent(Transform2D parent)
        {
            return new ShapeBox2D
            {
                center = math.rotate(center, parent.rotation) * parent.scale + parent.position,
                extends = extends * parent.scale,
                rotation = math.mul(rotation, parent.rotation)
            };
        }
    }
}