using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class TextureAtlasPacker: AutoDisposable
{
    private struct TextureItem
    {
        public string Name;
        public Texture2D Texture;
    }

    private readonly RenderingSystem _renderingSystem;
    private readonly RectPacker<TextureItem> _packer;
    private readonly PixelFormat _format;
    private readonly Material _blitMaterial;
    private readonly Camera2D _camera;
    private readonly GPUCommandBuffer _commandBuffer;

    internal TextureAtlasPacker(RenderingSystem rendering,
    PixelFormat format,
    Material blitMaterial,
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
        _commandBuffer = rendering.GraphicsDevice.CreateCommandBuffer("atlas_command_buffer");
        _camera = rendering.CreateCamera2D(minWidth, minHeight, 1000);
    }

    public void AddTexture(string name, Texture2D texture)
    {
        _packer.AddRect((int)texture.Width, (int)texture.Height, new TextureItem { Name = name, Texture = texture });
    }

    public TextureAtlas BuildTextureAtlas()
    {
        RenderTexture atlasTexture = _renderingSystem.CreateRenderTexture(
            _renderingSystem.PrefferedRGBATexturePass,
            (uint)_packer.Width,
            (uint)_packer.Height,
            "atlas_texture"
        );



        List<Sprite> sprites = new List<Sprite>();

        for (int i = 0; i < _packer.Count; i++)
        {
            var item = _packer.GetRect(i);
            sprites.Add(new Sprite(item.Data.Name, atlasTexture, item.Rect.Normalize(atlasTexture.Width, atlasTexture.Height), true));
        }

        uint width = atlasTexture.Width;
        uint height = atlasTexture.Height;

        _camera.Width = width;
        _camera.Height = height;
        _camera.Position = new Vector2(width / 2f, -height / 2f);
        _camera.UpdateBuffer();

        Mesh mesh = _renderingSystem.MeshSprite;

        ShaderPipelineInfo pipelineInfo = _blitMaterial.GetPipelineInfo(atlasTexture.RenderPass);
        uint shaderId_texture = pipelineInfo.ReflectionInfo.GetResourceId(ShaderResourceId.Texture);
        uint shaderId_camera = pipelineInfo.ReflectionInfo.GetResourceId(ShaderResourceId.Camera);

        SpriteConstant constant = new SpriteConstant
        {
            //Model = Matrix4x4.Identity,
            Color = ColorFloat.White,
            UvRect = new Rect(0, 0, 1, 1)
        };

        Transform2D transform = Transform2D.Identity;

        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(atlasTexture);
        _commandBuffer.ClearColor(ColorFloat.Black);
        _commandBuffer.SetGraphicsPipeline(pipelineInfo.Pipeline);
        _commandBuffer.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandBuffer.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);

        _commandBuffer.SetGraphicsResources(shaderId_camera, _camera.EntryReadonly);

        for (int i = 0; i < _packer.Count; i++)
        {
            var item = _packer.GetRect(i);
            transform.position = item.Rect.Center;
            transform.position.Y = -transform.position.Y;//the rect packer is start from top left
            transform.scale = item.Rect.size;
            constant.Model = transform.Matrix;

            _commandBuffer.SetGraphicsResources(shaderId_texture, item.Data.Texture.EntrySample);
            _commandBuffer.PushConstants(pipelineInfo.PushConstantsStages, constant);
            _commandBuffer.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }

        _commandBuffer.End();
        _renderingSystem.GraphicsDevice.Submit(_commandBuffer);


        return new TextureAtlas(atlasTexture, sprites);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _camera.Dispose();
            _packer.Dispose();
            _commandBuffer.Dispose();
        }
    }
}