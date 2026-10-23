using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    /// <summary>
    /// A 3D oriented box shape defined by a center, half-extents and rotation.
    /// </summary>
    public struct ShapeBox3D : IShape3D
    {
        public Vector3 Center;
        public Vector3 Extends;
        public Quaternion Rotation;

        /// <summary>
        /// Initializes an unrotated box with the given center and full size.
        /// </summary>
        /// <param name="center">The center of the box.</param>
        /// <param name="size">The full size of the box.</param>
        public ShapeBox3D(Vector3 center, Vector3 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = Quaternion.Identity;
        }

        /// <summary>
        /// Initializes a box with the given center, rotation and full size.
        /// </summary>
        /// <param name="center">The center of the box.</param>
        /// <param name="rotation">The rotation of the box.</param>
        /// <param name="size">The full size of the box.</param>
        public ShapeBox3D(Vector3 center, Quaternion rotation, Vector3 size)
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
        public ShapeBox3D(Vector3 center, Vector3 size, Quaternion rotation)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }


        /// <summary>
        /// Gets the axis-aligned bounds enclosing the rotated box.
        /// </summary>
        /// <returns>The bounding box of the shape.</returns>
        public BoundingBox3D GetBoundingBox()
        {
            if (Rotation.Equals(Quaternion.Identity))
            {
                return new BoundingBox3D(Center - Extends, Center + Extends);
            }

            Vector3 x = math.rotate(Rotation, new Vector3(Extends.X, 0, 0));
            Vector3 y = math.rotate(Rotation, new Vector3(0, Extends.Y, 0));
            Vector3 z = math.rotate(Rotation, new Vector3(0, 0, Extends.Z));

            Vector3 extentsInB = math.abs(x) + math.abs(y) + math.abs(z);
            // Vector3 extentsInB = math.abs(math.rotate(extends, rotation));

            return new BoundingBox3D(Center - extentsInB, Center + extentsInB);
        }

        /// <summary>
        /// Returns this shape transformed by the given parent transform.
        /// </summary>
        /// <param name="parent">The parent transform to apply.</param>
        /// <returns>The transformed shape.</returns>
        public ShapeBox3D TransformByParent(Transform3D parent)
        {
            return new ShapeBox3D
            {
                Center = math.rotate(parent.Rotation, Center) * parent.Scale + parent.Position,
                Extends = Extends * parent.Scale,
                Rotation = math.mul(parent.Rotation, Rotation),
            };
        }

        public override string ToString()
        {
            return $"Box: {Center} {Extends} {Rotation}";
        }
    }
}

