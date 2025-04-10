using System;
using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;
using Avalonia;
using Avalonia.Controls;

namespace Alco.Editor.Views;

/// <summary>
/// A specialized GPUSurfaceView for displaying textures
/// </summary>
public class TextureGPUSurfaceView : GPUSurfaceView
{
    private Texture2D? _texture;
    private readonly Material _material;
    private readonly RenderContext _renderContext;
    private readonly Camera2D _camera;
    private readonly Mesh _mesh;

    public BlendState BlendState
    {
        get => _material.BlendState;
        set
        {
            _material.BlendState = value;
            Log.Info($"BlendState changed to {value}");
        }
    }
    public Texture2D? Texture
    {
        get => _texture;
        set
        {
            if (_texture != value)
            {
                _texture = value;
                InvalidateVisual();
            }
        }
    }

    public TextureGPUSurfaceView() : base()
    {
        GameEngine engine = App.Main.Engine;

        // Create camera
        uint width = (uint)Math.Max(1, Bounds.Width);
        uint height = (uint)Math.Max(1, Bounds.Height);
        _camera = engine.Rendering.CreateCamera2D(width, height, 100);

        // Create sprite material
        _material = engine.Rendering.CreateGraphicsMaterial(engine.BuiltInAssets.Shader_Sprite);
        _material.BlendState = BlendState.AlphaBlend;
        _material.SetBuffer(ShaderResourceId.Camera, _camera);

        // Create rendering context and get sprite mesh
        _renderContext = engine.Rendering.CreateRenderContext();
        _mesh = engine.Rendering.MeshCenteredSprite;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Update camera to match control size
        if (_camera != null)
        {
            _camera.Width = (uint)Math.Max(1, e.NewSize.Width);
            _camera.Height = (uint)Math.Max(1, e.NewSize.Height);
            _camera.UpdateMatrixToGPU();
        }
    }


    protected override void OnRender(GPUFrameBuffer frameBuffer, float deltaTime)
    {
        if (_texture == null)
        {
            base.OnRender(frameBuffer, deltaTime);
            return;
        }

        Log.Info($"OnRender: {MousePosition}");

        uint width = (uint)Math.Max(1, Bounds.Width);
        uint height = (uint)Math.Max(1, Bounds.Height);

        float aspectRatioTexture = (float)_texture.Width / _texture.Height;
        float aspectRatioView = (float)(width / height);
        float scale;

        if (aspectRatioTexture > aspectRatioView)
        {
            // Texture is wider than view, scale height to fit
            scale = (float)height / _texture.Height;
        }
        else
        {
            // Texture is taller than view, scale width to fit
            scale = (float)width / _texture.Width;
        }

        scale *= 0.5f;

        _renderContext.Begin(frameBuffer);

        // Create transform and constant data
        Vector2 scaledSize = new Vector2(_texture.Width * scale, _texture.Height * scale);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, scaledSize);

        // Create sprite constant
        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new Vector4(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        // Set texture and draw with constant
        uint textureResourceId = _material.GetResourceId(ShaderResourceId.Texture);
        _material.SetTexture(textureResourceId, _texture);

        // Draw the texture directly with the render context
        _renderContext.DrawWithConstant(_mesh, _material, constant);

        _renderContext.End();
    }
}