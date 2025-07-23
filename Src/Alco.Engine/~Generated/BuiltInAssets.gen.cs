
// Auto generated code
using System;
using Alco.IO;
using Alco.GUI;
using Alco.Audio;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public partial class BuiltInAssets
{
    
    public Font Font_Default => GetFont("Fonts/Default.ttf");

    public Shader Shader_ClearTexture => GetShader("Shaders/Pipelines/Compute/ClearTexture.hlsl");

    public Shader Shader_FloodFillLighting => GetShader("Shaders/Pipelines/Compute/FloodFillLighting.hlsl");

    public Shader Shader_GaussianBlurRGBA16F => GetShader("Shaders/Pipelines/Compute/GaussianBlurRGBA16F.hlsl");

    public Shader Shader_GaussianBlurRGBA32F => GetShader("Shaders/Pipelines/Compute/GaussianBlurRGBA32F.hlsl");

    public Shader Shader_GaussianBlurRGBA8 => GetShader("Shaders/Pipelines/Compute/GaussianBlurRGBA8.hlsl");

    public Shader Shader_FXAA => GetShader("Shaders/Pipelines/PostProcess/FXAA.hlsl");

    public Shader Shader_Blit => GetShader("Shaders/Pipelines/Utils/Blit.hlsl");

    public Shader Shader_BlitDepth => GetShader("Shaders/Pipelines/Utils/BlitDepth.hlsl");

    public Shader Shader_TextureCompressBC3 => GetShader("Shaders/Pipelines/Compute/Compress/TextureCompressBC3.hlsl");

    public Shader Shader_BloomBlit => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomBlit.hlsl");

    public Shader Shader_BloomClamp => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomClamp.hlsl");

    public Shader Shader_BloomDownSample => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomDownSample.hlsl");

    public Shader Shader_BloomUpSample => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomUpSample.hlsl");

    public Shader Shader_ReinhardLuminanceTonemap => GetShader("Shaders/Pipelines/PostProcess/ToneMap/ReinhardLuminanceTonemap.hlsl");

    public Shader Shader_Uncharted2Tonemap => GetShader("Shaders/Pipelines/PostProcess/ToneMap/Uncharted2Tonemap.hlsl");

    public Shader Shader_Unlit => GetShader("Shaders/Pipelines/Rendering/Basic/Unlit.hlsl");

    public Shader Shader_Particle2D => GetShader("Shaders/Pipelines/Rendering/Particle/Particle2D.hlsl");

    public Shader Shader_Sprite => GetShader("Shaders/Pipelines/Rendering/Sprite/Sprite.hlsl");

    public Shader Shader_SpriteInstanced => GetShader("Shaders/Pipelines/Rendering/Sprite/SpriteInstanced.hlsl");

    public Shader Shader_Text => GetShader("Shaders/Pipelines/Rendering/Text/Text.hlsl");

    public Shader Shader_TileConnectable => GetShader("Shaders/Pipelines/Rendering/TileMap/TileConnectable.hlsl");

    public Shader Shader_TileInstanced => GetShader("Shaders/Pipelines/Rendering/TileMap/TileInstanced.hlsl");

    public Shader Shader_TileInstancedWithHeight => GetShader("Shaders/Pipelines/Rendering/TileMap/TileInstancedWithHeight.hlsl");

    public Shader Shader_TileLighting => GetShader("Shaders/Pipelines/Rendering/TileMap/TileLighting.hlsl");

    public Shader Shader_TileWaterInstanced => GetShader("Shaders/Pipelines/Rendering/TileMap/TileWaterInstanced.hlsl");


}

