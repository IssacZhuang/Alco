using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using Unity.Jobs;

namespace Vocore
{
    public unsafe struct JobTransMatrixFromBuffer : IJobParallelFor
    {
        public StructuredBuffer<Vector3> positions;
        public StructuredBuffer<Quaternion> rotations;
        public StructuredBuffer<Vector3> scales;

        public StructuredBuffer<Matrix4x4> matrices;

        public void Execute(int index)
        {
            matrices[index].SetTRS(positions[index], rotations[index], scales[index]);
        }
    }
}
