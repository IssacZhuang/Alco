using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Vocore
{
    public unsafe struct JobTransfromMatrix : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction]
        public Vector3* positions;
        [NativeDisableUnsafePtrRestriction]
        public Quaternion* rotations;
        [NativeDisableUnsafePtrRestriction]
        public Vector3* scales;

        [NativeDisableUnsafePtrRestriction]
        public Matrix4x4* matrices;
        public void Execute(int index)
        {
            matrices[index].SetTRS(positions[index], rotations[index], scales[index]);
        }
    }
}
