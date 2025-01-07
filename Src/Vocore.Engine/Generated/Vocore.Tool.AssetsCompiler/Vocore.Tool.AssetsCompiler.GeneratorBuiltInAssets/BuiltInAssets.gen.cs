
// Auto generated code
using System;
using Vocore.IO;
using Vocore.GUI;
using Vocore.Audio;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public partial class BuiltInAssets
{
    
    public Font Font_Default => GetFont("Fonts/Default.ttf");

    public Shader Shader_Sprite => GetShader("Shaders/Pipelines/2D/Sprite.hlsl");

    public Shader Shader_SpriteMasked => GetShader("Shaders/Pipelines/2D/SpriteMasked.hlsl");

    public Shader Shader_Text => GetShader("Shaders/Pipelines/2D/Text.hlsl");

    public Shader Shader_TextMasked => GetShader("Shaders/Pipelines/2D/TextMasked.hlsl");

    public Shader Shader_TiledTerrain => GetShader("Shaders/Pipelines/2D/TiledTerrain.hlsl");

    public Shader Shader_Unlit => GetShader("Shaders/Pipelines/3D/Unlit.hlsl");

    public Shader Shader_Blit => GetShader("Shaders/Pipelines/Common/Blit.hlsl");

    public Shader Shader_Wireframe => GetShader("Shaders/Pipelines/Common/Wireframe.hlsl");

    public Shader Shader_BloomBlit => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomBlit.hlsl");

    public Shader Shader_BloomClamp => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomClamp.hlsl");

    public Shader Shader_BloomDownSample => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomDownSample.hlsl");

    public Shader Shader_BloomUpSample => GetShader("Shaders/Pipelines/PostProcess/Bloom/BloomUpSample.hlsl");

    public Shader Shader_ReinhardLuminanceTonemap => GetShader("Shaders/Pipelines/ToneMap/ReinhardLuminanceTonemap.hlsl");

    public Shader Shader_Uncharted2Tonemap => GetShader("Shaders/Pipelines/ToneMap/Uncharted2Tonemap.hlsl");


}

