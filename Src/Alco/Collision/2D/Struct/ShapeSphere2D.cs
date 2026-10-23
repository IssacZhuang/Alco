using System;
using System.Numerics;

namespace Alco
{
    /// <summary>
        /// A 2D sphere shape defined by a center and radius.
    /// </summary>
    public struct ShapeSphere2D : IShape2D
    {
        public Vector2 Center;
        public float Radius;

        /// <summary>
        /// Initializes a sphere with the given center and radius.
        /// </summary>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        public ShapeSphere2D(Vector2 center, float radius)
        {
            this.Center = center;
            this.Radius = radius;
        }

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the sphere.
        /// </summary>
        /// <returns>The bounding box of the shape.</returns>
        public BoundingBox2D GetBoundingBox()
        {
            return new BoundingBox2D(Center - new Vector2(Radius), Center + new Vector2(Radius));
        }

        /// <summary>
        /// Returns this shape transformed by the given parent transform.
        /// </summary>
        /// <param name="parent">The parent transform to apply.</param>
        /// <returns>The transformed shape.</returns>
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