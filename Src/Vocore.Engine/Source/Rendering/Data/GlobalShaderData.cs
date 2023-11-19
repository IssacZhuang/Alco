using System;
using System.Numerics;
using Veldrid;
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
        public static readonly uint SizeInBytes = DeviceBufferHelper.GetUniformBufferSize<GlobalShaderData>();
        public Matrix4x4 viewProjMatrix;
        public Vector2 screenSize;
        public float time;
        public float deltaTime;
        public float sinTime;
        public float cosTime;

        //pre element
        public static readonly ResourceLayoutDescription Layout = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("GlobalBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)
        );
    }

    
}