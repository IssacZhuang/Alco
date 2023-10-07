using System;
using System.Numerics;

namespace Vocore{
    public struct RigidTransform{
        public Vector3 pos;
        public Quaternion rot;

        public RigidTransform(Vector3 pos, Quaternion rot){
            this.pos = pos;
            this.rot = rot;
        }

        public RigidTransform(Quaternion rot, Vector3 pos){
            this.pos = pos;
            this.rot = rot;
        }
    }
}