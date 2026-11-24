#language slang 2025
module AlcoRendering_SpriteScene;

import Core;
import SharedSampler;

public static const uint SPRITE_SCENE_LOCAL_AGGREGATE_BUCKETS = 64u;

public static const uint SPRITE_SCENE_SAMPLER_MASK = 3u;
public static const uint SPRITE_SCENE_SAMPLER_LINEAR_REPEAT = 0u;
public static const uint SPRITE_SCENE_SAMPLER_LINEAR_CLAMP = 1u;
public static const uint SPRITE_SCENE_SAMPLER_NEAREST_REPEAT = 2u;
public static const uint SPRITE_SCENE_SAMPLER_NEAREST_CLAMP = 3u;

public static const uint SPRITE_SCENE_FLAG_PREMULTIPLIED = 1u << 2;

public struct SpriteSceneInstance
{
    public float2 Position;
    public float2 Rotation; // (sin, cos) matching Alco.Rotation2D
    public float2 Scale;
    public float Depth;
    public uint Flags;      // bits 0..1 sampler, bit 2 premultiplied, bits 3..31 caller surface flags
    public float4 Color;
    public float4 UvRect;
    public float4 CustomData;
    public uint TextureId;
    public uint AuxTextureId;
    public uint MaterialParamId;
    public uint UserData;
};

public struct SpriteSceneBucketMeta
{
    public uint VertexCount;
    public uint VisibleBase;
    public float LocalExtentScale;
    public uint BucketTag;
};

public struct SpriteScenePassHeader
{
    public float4x4 ViewProjection;
    public GPUBufferView Pool;
    public GPUBufferView VisibleSlots;
    public GPUBufferView MaterialParams;
    public float Time;
    public uint BucketTag;
    public float4 UserParams;
    public uint HasMaterialParams;
    public uint Pad0;
    public float2 OriginOffset;
};

public struct EmptySpriteSceneParams
{
    public uint _pad;
};

public struct SpriteSceneLocalVertex
{
    public float3 Position;
    public float2 Uv;
};

public struct SpriteSceneVertexInput
{
    public uint VertexId;
    public float Time;
    public uint BucketTag;
    public float2 Position;
    public float2 Rotation;
    public float2 Scale;
    public float Depth;
    public uint Flags;
    public uint UserData;
    public float4 CustomData;
    public uint MaterialParamId;
    public GPUBufferView MaterialParams;
    public uint HasMaterialParams;
};

public struct SpriteSceneSurfaceInput
{
    public float2 Uv;
    public float2 LocalUv;
    public float3 WorldPos;
    public float2 ScreenPosition;
    public float2 DdxUv;
    public float2 DdyUv;
    public float4 Color;
    public float4 CustomData;
    public uint TextureId;
    public uint AuxTextureId;
    public uint Flags;
    public uint MaterialParamId;
    public uint BucketTag;
    public uint UserData;
    public float Time;
    public float4 UserParams;
    public GPUBufferView MaterialParams;
    public uint HasMaterialParams;
};

public struct SpriteSceneVsOut
{
    public float4 Position : SV_Position;
    public float2 Uv : TEXCOORD0;
    public float2 LocalUv : TEXCOORD1;
    public float3 WorldPos : TEXCOORD2;
    public float4 Color : COLOR0;
    public float4 CustomData : TEXCOORD3;
    public nointerpolation uint TextureId : TEXCOORD4;
    public nointerpolation uint AuxTextureId : TEXCOORD5;
    public nointerpolation uint Flags : TEXCOORD6;
    public nointerpolation uint MaterialParamId : TEXCOORD7;
    public nointerpolation uint UserData : TEXCOORD8;
};

public interface ISpriteSceneSurface
{
    associatedtype Params;

    SpriteSceneLocalVertex ExpandLocalVertex(uint vertexId, SpriteSceneVertexInput input, Params p)
    {
        return spriteSceneExpandUnitQuad(vertexId);
    }

    float3 TransformToWorld(SpriteSceneLocalVertex vertex, SpriteSceneVertexInput input, Params p)
    {
        return spriteSceneTransform2D(vertex.Position, input.Position, input.Rotation, input.Scale, input.Depth);
    }

    float4 GetColor(SpriteSceneSurfaceInput input, Params p);

    float GetAlphaCutoff(Params p)
    {
        return 0.5;
    }
};

// Matches Alco.math.rotate(Vector2, Rotation2D) left-handed 2D rotation where rotSC = (sin, cos).
public float2 spriteSceneRotate(float2 v, float2 rotSC)
{
    return float2(rotSC.y * v.x + rotSC.x * v.y, rotSC.y * v.y - rotSC.x * v.x);
}

public SpriteSceneLocalVertex spriteSceneExpandUnitQuad(uint vertexId)
{
    uint corner = vertexId % 6u;
    SpriteSceneLocalVertex result;
    switch (corner)
    {
    case 0u: result.Position = float3(-0.5, 0.5, 0.0); result.Uv = float2(0.0, 0.0); break;
    case 1u: result.Position = float3(0.5, 0.5, 0.0); result.Uv = float2(1.0, 0.0); break;
    case 2u: result.Position = float3(0.5, -0.5, 0.0); result.Uv = float2(1.0, 1.0); break;
    case 3u: result.Position = float3(-0.5, 0.5, 0.0); result.Uv = float2(0.0, 0.0); break;
    case 4u: result.Position = float3(0.5, -0.5, 0.0); result.Uv = float2(1.0, 1.0); break;
    default: result.Position = float3(-0.5, -0.5, 0.0); result.Uv = float2(0.0, 1.0); break;
    }
    return result;
}

public float3 spriteSceneTransform2D(
    float3 localPos,
    float2 position,
    float2 rotation,
    float2 scale,
    float depth)
{
    float2 xy = spriteSceneRotate(localPos.xy * scale, rotation) + position;
    return float3(xy, localPos.z + depth);
}

public float4 spriteSceneSampleTexture(uint textureId, uint flags, float2 uv, float2 ddxUv, float2 ddyUv)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(textureId));
    uint samplerMode = flags & SPRITE_SCENE_SAMPLER_MASK;
    if (samplerMode == SPRITE_SCENE_SAMPLER_LINEAR_CLAMP)
        return tex.SampleGrad(shared_sampler_linear_clamp(), uv, ddxUv, ddyUv);
    if (samplerMode == SPRITE_SCENE_SAMPLER_NEAREST_REPEAT)
        return tex.SampleGrad(shared_sampler_nearest_repeat(), uv, ddxUv, ddyUv);
    if (samplerMode == SPRITE_SCENE_SAMPLER_NEAREST_CLAMP)
        return tex.SampleGrad(shared_sampler_nearest_clamp(), uv, ddxUv, ddyUv);
    return tex.SampleGrad(shared_sampler_linear_repeat(), uv, ddxUv, ddyUv);
}
