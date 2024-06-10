
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
    
    public Font Font_Default => GetFont("Font/Default.ttf");

    public Shader Shader_Sprite => GetShader("Rendering/Shader/2D/Sprite.hlsl");

    public Shader Shader_SpriteAlphaClip => GetShader("Rendering/Shader/2D/SpriteAlphaClip.hlsl");

    public Shader Shader_SpriteMasked => GetShader("Rendering/Shader/2D/SpriteMasked.hlsl");

    public Shader Shader_Text => GetShader("Rendering/Shader/2D/Text.hlsl");

    public Shader Shader_TextAlphaClip => GetShader("Rendering/Shader/2D/TextAlphaClip.hlsl");

    public Shader Shader_TextMasked => GetShader("Rendering/Shader/2D/TextMasked.hlsl");

    public Shader Shader_Unlit => GetShader("Rendering/Shader/3D/Unlit.hlsl");

    public Shader Shader_Blit => GetShader("Rendering/Shader/Common/Blit.hlsl");

    public Shader Shader_WireFrame => GetShader("Rendering/Shader/Common/WireFrame.hlsl");

    public Shader Shader_BloomBlit => GetShader("Rendering/Shader/PostProcess/Bloom/BloomBlit.hlsl");

    public Shader Shader_BloomClamp => GetShader("Rendering/Shader/PostProcess/Bloom/BloomClamp.hlsl");

    public Shader Shader_BloomDownSample => GetShader("Rendering/Shader/PostProcess/Bloom/BloomDownSample.hlsl");

    public Shader Shader_BloomUpSample => GetShader("Rendering/Shader/PostProcess/Bloom/BloomUpSample.hlsl");

    public Shader Shader_ReinhardLuminanceTonemap => GetShader("Rendering/Shader/ToneMap/ReinhardLuminanceTonemap.hlsl");

    public Shader Shader_Uncharted2Tonemap => GetShader("Rendering/Shader/ToneMap/Uncharted2Tonemap.hlsl");


}

