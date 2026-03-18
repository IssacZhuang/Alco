using Alco.Graphics;
using StbImageWriteSharp;

namespace Alco.Rendering;

/// <summary>
/// Provides PNG encoding functionality for textures with optional resizing.
/// Converts textures of any format to RGBA8 and encodes them to PNG format.
/// </summary>
public sealed class TextureEncoderPNG : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly Material _blitMaterial;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;

    private RenderTexture? _cachedRenderTexture;
    private uint _cachedWidth;
    private uint _cachedHeight;
    private NativeBuffer<byte> _cachedData;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureEncoderPNG"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance.</param>
    /// <param name="blitMaterial">The material used for blitting textures to the render texture.</param>
    internal TextureEncoderPNG(RenderingSystem renderingSystem, Material blitMaterial)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _blitMaterial = blitMaterial;
        _renderContext = renderingSystem.CreateRenderContext("texture_encoder_png_render_context");
        _fullScreenMesh = renderingSystem.MeshFullScreen;
    }

    /// <summary>
    /// Encodes the specified texture to PNG format and writes it to the output stream.
    /// </summary>
    /// <param name="source">The source texture to encode.</param>
    /// <param name="output">The output stream to write the PNG data.</param>
    public void Encode(Texture2D source, Stream output)
    {
        EncodeCore(source, source.Width, source.Height, output);
    }

    /// <summary>
    /// Encodes the specified texture to PNG format with the target size and writes it to the output stream.
    /// </summary>
    /// <param name="source">The source texture to encode.</param>
    /// <param name="output">The output stream to write the PNG data.</param>
    /// <param name="targetSize">The target size for the encoded texture.</param>
    public void Encode(Texture2D source, Stream output, int2 targetSize)
    {
        EncodeCore(source, (uint)targetSize.X, (uint)targetSize.Y, output);
    }

    private unsafe void EncodeCore(Texture2D source, uint targetWidth, uint targetHeight, Stream output)
    {
        EnsureRenderTexture(targetWidth, targetHeight);

        _blitMaterial.SetTexture(ShaderResourceId.Texture, source);

        _renderContext.Begin(_cachedRenderTexture!.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _blitMaterial);
        _renderContext.End();

        _device.ReadTexture(_cachedRenderTexture.ColorTextures[0].NativeTexture, _cachedData.UnsafePointer, (uint)_cachedData.Length);

        ImageWriter writer = new ImageWriter();
        writer.WritePng(_cachedData.UnsafePointer, (int)targetWidth, (int)targetHeight, StbImageSharp.ColorComponents.RedGreenBlueAlpha, output);
    }

    private void EnsureRenderTexture(uint width, uint height)
    {
        if (_cachedRenderTexture != null && _cachedWidth == width && _cachedHeight == height)
        {
            return;
        }

        _cachedRenderTexture?.Dispose();

        _cachedWidth = width;
        _cachedHeight = height;
        _cachedRenderTexture = _renderingSystem.CreateRenderTexture(
            _renderingSystem.PrefferedRGBATexturePass,
            width,
            height,
            "texture_encoder_png_render_texture"
        );
        _cachedData.SetSizeWithoutCopy((int)(width * height * 4));
    }

    protected override void Dispose(bool disposing)
    {
        _cachedData.Dispose();
        if (disposing)
        {
            _renderContext.Dispose();
            _cachedRenderTexture?.Dispose();
        }
    }
}
