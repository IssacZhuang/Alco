using System;
using System.Numerics;

namespace Alco
{
    /// <summary>
    /// A 2D oriented box shape defined by a center, half-extents and rotation.
    /// </summary>
    public struct ShapeBox2D : IShape2D
    {
        public Vector2 Center;
        public Vector2 Extends;
        public Rotation2D Rotation;

        /// <summary>
        /// Initializes an unrotated box with the given center and full size.
        /// </summary>
        /// <param name="center">The center of the box.</param>
        /// <param name="size">The full size of the box.</param>
        public ShapeBox2D(Vector2 center, Vector2 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = Rotation2D.Identity;
        }

        /// <summary>
        /// Initializes a box with the given center, rotation and full size.
        /// </summary>
        /// <param name="center">The center of the box.</param>
        /// <param name="rotation">The rotation of the box.</param>
        /// <param name="size">The full size of the box.</param>
        public ShapeBox2D(Vector2 center, Rotation2D rotation, Vector2 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }

        /// <summary>
        /// Initializes a box with the given center, full size and rotation.
        /// </summary>
        /// <param name="center">The center of the box.</param>
        /// <param name="size">The full size of the box.</param>
        /// <param name="rotation">The rotation of the box.</param>
        public ShapeBox2D(Vector2 center, Vector2 size, Rotation2D rotation)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the rotated box.
        /// </summary>
        /// <returns>The bounding box of the shape.</returns>
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

        /// <summary>
        /// Returns this shape transformed by the given parent transform.
        /// </summary>
        /// <param name="parent">The parent transform to apply.</param>
        /// <returns>The transformed shape.</returns>
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