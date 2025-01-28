
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

    public Shader Shader_Blit => GetShader("Shaders/Pipelines/Utils/Blit.hlsl");

    public Shader Shader_BloomBlit => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomBlit.hlsl");

    public Shader Shader_BloomClamp => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomClamp.hlsl");

    public Shader Shader_BloomDownSample => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomDownSample.hlsl");

    public Shader Shader_BloomUpSample => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomUpSample.hlsl");

    public Shader Shader_ReinhardLuminanceTonemap => GetShader("Shaders/Pipelines/PostProcess/ToneMap/ReinhardLuminanceTonemap.hlsl");

    public Shader Shader_Uncharted2Tonemap => GetShader("Shaders/Pipelines/PostProcess/ToneMap/Uncharted2Tonemap.hlsl");

    public Shader Shader_Unlit => GetShader("Shaders/Pipelines/Rendering/Basic/Unlit.hlsl");

    public Shader Shader_Sprite => GetShader("Shaders/Pipelines/Rendering/Sprite/Sprite.hlsl");

    public Shader Shader_SpriteMasked => GetShader("Shaders/Pipelines/Rendering/Sprite/SpriteMasked.hlsl");

    public Shader Shader_Text => GetShader("Shaders/Pipelines/Rendering/Text/Text.hlsl");

    public Shader Shader_TextMasked => GetShader("Shaders/Pipelines/Rendering/Text/TextMasked.hlsl");

    public Shader Shader_TilePlant => GetShader("Shaders/Pipelines/Rendering/TileMap/TilePlant.hlsl");

    public Shader Shader_TileSurface => GetShader("Shaders/Pipelines/Rendering/TileMap/TileSurface.hlsl");

    public Shader Shader_TileWater => GetShader("Shaders/Pipelines/Rendering/TileMap/TileWater.hlsl");


}

