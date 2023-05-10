using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public static class UtilsTranform
    {
        public static RigidTransform ToLocal(RigidTransform transform, RigidTransform parent)
        {
            RigidTransform parentInverse = math.inverse(parent);
            float3 localPosition = math.mul(parentInverse.rot, transform.pos - parent.pos);
            quaternion localRotation = math.mul(parentInverse.rot, transform.rot);
            return new RigidTransform(localRotation, localPosition);
        }
    }
}

