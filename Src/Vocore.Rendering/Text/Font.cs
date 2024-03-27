using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// A font atlas texture with unicode to glyph mapping.
/// </summary>
public unsafe class Font : Texture2D
{
    private readonly GlyphInfo[] _glyphs;

    internal Font(GPUDevice device, GPUTexture texture, GPUTextureView textureView, GPUSampler sampler, GlyphInfo[] glyphs) : base(device, texture, textureView, sampler)
    {
        _glyphs = glyphs;
    }

    public GlyphInfo GetGlyph(char c)
    {
        return _glyphs[c];
    }

    public static Font CreateFont(MemoryRef<byte> bitmap, int width, int height, GlyphInfo[] glyphs, string name = "font")
    {
        GPUDevice device = GetDevice();
        TextureDescriptor textureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D,
            PixelFormat.R8Unorm,
            (uint)width,
            (uint)height,
            1,
            1,
            TextureUsage.Standard,
            1,
            name
        );

        GPUTexture texture = device.CreateTexture(textureDescriptor);

        TextureViewDescriptor textureViewDescriptor = new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D
        );

        textureDescriptor.Name = name;

        GPUTextureView textureView = device.CreateTextureView(textureViewDescriptor);

        Font font =new Font(
            device,
            texture,
            textureView,
            device.SamplerLinearClamp,
            glyphs
        );

        font.SetPixels(bitmap.Pointer, bitmap.Length, 1);

        return font;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose();
    }
}