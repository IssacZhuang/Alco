using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed class TextureAtlasPacker: AutoDisposable
{
    private struct TextureItem
    {
        public string Name;
        public Texture2D Texture;
    }

    private readonly RenderingSystem _renderingSystem;
    private readonly RectPacker<TextureItem> _packer;
    private readonly PixelFormat _format;
    private readonly GraphicsMaterial _blitMaterial;
    private readonly Camera2DBuffer _camera;
    private readonly RenderContext _renderContext;

    internal TextureAtlasPacker(RenderingSystem rendering,
    PixelFormat format,
    GraphicsMaterial blitMaterial,
    //it just initial size
    int minWidth = 256,
    int minHeight = 256
    )
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(blitMaterial);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minHeight);

        _renderingSystem = rendering;
        _packer = new RectPacker<TextureItem>(minWidth, minHeight);
        _format = format;
        _blitMaterial = blitMaterial;
        _renderContext = rendering.CreateRenderContext("atlas_render_context");
        _camera = rendering.CreateCamera2D(minWidth, minHeight, 1000);
    }

    public void AddTexture(string name, Texture2D texture)
    {
        _packer.AddRect((int)texture.Width, (int)texture.Height, new TextureItem { Name = name, Texture = texture });
    }

    public TextureAtlas BuildTextureAtlas()
    {
        RenderTexture atlasTexture = _renderingSystem.CreateRenderTexture(
            _renderingSystem.PreferredRGBATexturePass,
            (uint)_packer.Width,
            (uint)_packer.Height,
            "atlas_texture"
        );

        List<Sprite> sprites = new List<Sprite>();

        for (int i = 0; i < _packer.Count; i++)
        {
            var item = _packer.GetRect(i);
            sprites.Add(new Sprite(item.Data.Name, atlasTexture.ColorTextures[0], item.Rect.Normalize(atlasTexture.Width, atlasTexture.Height)));
        }

        uint width = atlasTexture.Width;
        uint height = atlasTexture.Height;

        _camera.Width = width;
        _camera.Height = height;
        _camera.Position = new Vector2(width / 2f, -height / 2f);
        _camera.UpdateBuffer();

        Mesh mesh = _renderingSystem.MeshCenteredSprite;

        // A private instance of the caller's material: the atlas' camera and the
        // per-item texture bindings below must not leak into the caller's material.
        using GraphicsMaterial material = _blitMaterial.CreateInstance();
        material.SetBuffer(ShaderResourceId.Camera, _camera);

        SpriteConstant constant = new SpriteConstant
        {
            //Model = Matrix4x4.Identity,
            Color = ColorFloat.White,
            UvRect = new Rect(0, 0, 1, 1)
        };

        Transform2D transform = Transform2D.Identity;

        using (RenderFrameScope frame = _renderContext.BeginFrame())
        using (RenderPassScope renderPass = _renderContext.BeginPass(
            atlasTexture.FrameBuffer, [new ClearColorData(0, ColorFloat.Black)]))
        {
            for (int i = 0; i < _packer.Count; i++)
            {
                var item = _packer.GetRect(i);
                transform.Position = item.Rect.Center;
                transform.Position.Y = -transform.Position.Y;//the rect packer is start from top left
                transform.Scale = item.Rect.Size;
                constant.Model = transform.Matrix;

                material.SetTexture(ShaderResourceId.Texture, item.Data.Texture);
                renderPass.DrawWithConstant(mesh, material, constant);
            }
        }

        return new TextureAtlas(atlasTexture, sprites);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _camera.Dispose();
            _packer.Dispose();
            _renderContext.Dispose();
        }
    }
}
