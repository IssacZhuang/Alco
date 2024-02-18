using System;
using System.Numerics;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    // layout(set = 0, binding = 0) uniform GlobalBuffer
    // {
    //     mat4 _ViewProjMatrix;
    //     vec2 _ScreenSize;
    //     float _Time;
    //     float _DeltaTime;
    //     float _SinTime;
    //     float _CosTime;
    // };
    public struct GlobalShaderData
    {
        public Vector2 screenSize;
        public Vector2 screenSizeInv;
        public float time;
        public float deltaTime;
        public float sinTime;
        public float cosTime;
        public Matrix4x4 camera3D;
        public Matrix3x3 camera2D;

    }

    
}