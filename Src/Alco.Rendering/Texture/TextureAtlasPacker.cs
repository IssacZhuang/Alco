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
    private readonly Material _blitMaterial;
    private readonly Camera2DBuffer _camera;
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

    public TextureAtlas BuildTextureAtlas(GPUSampler? sampler = null)
    {
        RenderTexture atlasTexture;
        if (sampler == null)
        {
            atlasTexture = _renderingSystem.CreateRenderTexture(
            _renderingSystem.PrefferedRGBATexturePass,
            (uint)_packer.Width,
            (uint)_packer.Height,
                "atlas_texture"
            );
        }
        else
        {
            atlasTexture = _renderingSystem.CreateRenderTexture(
                _renderingSystem.PrefferedRGBATexturePass,
                (uint)_packer.Width,
                (uint)_packer.Height,
                sampler,
                "atlas_texture"
            );
        }

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

        ShaderPipelineInfo pipelineInfo = _blitMaterial.GetPipelineInfo(atlasTexture.AttachmentLayout);
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
    
        // _commandBuffer.SetFrameBuffer(atlasTexture);
        // _commandBuffer.ClearColor(ColorFloat.Black);
        // _commandBuffer.SetGraphicsPipeline(pipelineInfo.Pipeline);
        // uint indexCount = _commandBuffer.SetMesh(mesh);

        // _commandBuffer.SetGraphicsResources(shaderId_camera, _camera.EntryReadonly);

        // for (int i = 0; i < _packer.Count; i++)
        // {
        //     var item = _packer.GetRect(i);
        //     transform.Position = item.Rect.Center;
        //     transform.Position.Y = -transform.Position.Y;//the rect packer is start from top left
        //     transform.Scale = item.Rect.Size;
        //     constant.Model = transform.Matrix;

        //     _commandBuffer.SetGraphicsResources(shaderId_texture, item.Data.Texture.EntrySample);
        //     _commandBuffer.PushGraphicsConstants(pipelineInfo.PushConstantsStages, constant);
        //     _commandBuffer.DrawIndexed(indexCount, 1, 0, 0, 0);
        // }

        using (var renderScope = _commandBuffer.BeginRender(atlasTexture.FrameBuffer, [new ClearColorData(0, ColorFloat.Black)]))
        {
            renderScope.SetGraphicsPipeline(pipelineInfo.Pipeline);
            uint indexCount = renderScope.SetMesh(mesh);

            renderScope.SetGraphicsResources(shaderId_camera, _camera.EntryReadonly);

            for (int i = 0; i < _packer.Count; i++)
            {
                var item = _packer.GetRect(i);
                transform.Position = item.Rect.Center;
                transform.Position.Y = -transform.Position.Y;//the rect packer is start from top left
                transform.Scale = item.Rect.Size;
                constant.Model = transform.Matrix;

                renderScope.SetGraphicsResources(shaderId_texture, item.Data.Texture.EntrySample);
                renderScope.PushGraphicsConstants(pipelineInfo.PushConstantsStages, constant);
                renderScope.DrawIndexed(indexCount, 1, 0, 0, 0);
            }

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