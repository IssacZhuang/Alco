using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public struct ShapeBox: IShape
    {
        public float3 center;
        public float3 extends;
        public quaternion rotation;

        public ShapeBox(float3 center, float3 size, quaternion rotation)
        {
            this.center = center;
            this.extends = size * 0.5f;
            this.rotation = rotation;
        }


        public BoundingBox GetBoundingBox()
        {
            if (rotation.Equals(quaternion.identity))
            {
                return new BoundingBox(center - extends, center + extends);
            }

            float3 x = math.rotate(rotation, new float3(extends.x, 0, 0));
            float3 y = math.rotate(rotation, new float3(0, extends.y, 0));
            float3 z = math.rotate(rotation, new float3(0, 0, extends.z));

            float3 halfExtentsInB = math.abs(x) + math.abs(y) + math.abs(z);

            return new BoundingBox(center - halfExtentsInB, center + halfExtentsInB);
        }

        public BoundingBox GetBoundingBox(RigidTransform transform)
        {
            float3 centerInWorld = math.transform(transform, center);
            quaternion rotationInWorld = math.mul(transform.rot, rotation);
            
            if (rotationInWorld.Equals(quaternion.identity))
            {
                return new BoundingBox(centerInWorld - extends, centerInWorld + extends);
            }

            float3 x = math.rotate(rotationInWorld, new float3(extends.x, 0, 0));
            float3 y = math.rotate(rotationInWorld, new float3(0, extends.y, 0));
            float3 z = math.rotate(rotationInWorld, new float3(0, 0, extends.z));

            float3 halfExtentsInB = math.abs(x) + math.abs(y) + math.abs(z);

            return new BoundingBox(centerInWorld - halfExtentsInB, centerInWorld + halfExtentsInB);
        }
    }
}

