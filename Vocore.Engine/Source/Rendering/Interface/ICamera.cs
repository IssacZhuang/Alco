using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vocore.Engine
{
    public interface ICamera
    {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Matrix4x4 ViewMatrix { get; }
        public Matrix4x4 ProjectionMatrix { get; }
        public Matrix4x4 ViewProjectionMatrix { get; }
    }
}

