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
        public static readonly uint SizeInBytes = GetSizeInBytes();
        
        public static uint GetSizeInBytes()
        {
            uint size = (uint)UtilsMemory.SizeOf<GlobalShaderData>();
            uint remainder = size % 16;
            //Uniform buffer size must be a multiple of 16 bytes.
            size += remainder == 0 ? 0 : 16 - remainder;
            return size;
        }
    }

    
}