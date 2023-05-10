using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public static class UtilsTranform
    {
        public static RigidTransform ToLocal(RigidTransform transform, RigidTransform parent)
        {
            float3 localPosition = math.transform(parent, transform.pos);
            quaternion localRotation = math.mul(parent.rot, transform.rot);
            return new RigidTransform(localRotation, localPosition);
        }
    }
}

