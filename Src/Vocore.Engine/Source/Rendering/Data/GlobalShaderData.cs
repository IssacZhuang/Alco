using System;
using System.Numerics;
using Vocore.Unsafe;

namespace Vocore.Engine
{

    public struct GlobalShaderData
    {
        public Vector2 screenSize;
        public Vector2 screenSizeInv;
        public float time;
        public float deltaTime;
        public float sinTime;
        public float cosTime;
        public Matrix4x4 camera;

    }

    
}