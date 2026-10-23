using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    /// <summary>
    /// A 3D sphere shape defined by a center and radius.
    /// </summary>
    public struct ShapeSphere3D : IShape3D
    {
        public Vector3 Center;
        public float Radius;

        /// <summary>
        /// Initializes a sphere with the given center and radius.
        /// </summary>
        /// <param name="center">The center of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        public ShapeSphere3D(Vector3 center, float radius)
        {
            this.Center = center;
            this.Radius = radius;
        }

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the sphere.
        /// </summary>
        /// <returns>The bounding box of the shape.</returns>
        public BoundingBox3D GetBoundingBox()
        {
            Vector3 extends = new Vector3(Radius);
            return new BoundingBox3D(Center - extends, Center + extends);
        }

        /// <summary>
        /// Returns this shape transformed by the given parent transform.
        /// </summary>
        /// <param name="parent">The parent transform to apply.</param>
        /// <returns>The transformed shape.</returns>
        public ShapeSphere3D TransformByParent(Transform3D parent)
        {
            return new ShapeSphere3D
            {
                Center = math.rotate(parent.Rotation, Center) * parent.Scale + parent.Position,
                Radius = Radius * math.max(parent.Scale.X, math.max(parent.Scale.Y, parent.Scale.Z))
            };
        }


        public override string ToString()
        {
            return $"Sphere: {Center}, {Radius}";
        }

    }
}

