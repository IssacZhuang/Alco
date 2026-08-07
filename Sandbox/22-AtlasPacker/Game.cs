using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using FastRandom = Alco.FastRandom;
using Alco.Graphics;
using Alco.GUI;

public class Game : GameEngine
{
    private readonly ForwardPipeline _mainPipeline;
    private readonly TextureAtlas _atlas;
    private readonly RenderContext _materialRenderer;
    private readonly Camera2DBuffer _camera;
    private readonly Material _material;

    public GPUFrameBuffer MainFrameBuffer => _mainPipeline.SceneFrameBuffer;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(RenderingSystem, RenderingSystem.PreferredSDRPass, BuiltInAssets.Shader_Blit, MainView.Size.X, MainView.Size.Y);

        FastRandom random = new FastRandom(123456789);
        int spriteCount = 32;
        List<int2> spriteSizes = new List<int2>();
        List<Texture2D> textures = new List<Texture2D>();
        for (int i = 0; i < spriteCount; i++)
        {
            uint width = random.NextUint(1, 128);
            uint height = random.NextUint(1, 128);
            spriteSizes.Add(new int2(width, height));
            Texture2D texture = RenderingSystem.CreateTexture2D(
                width, 
                height, 
                new Color32(random.NextByte(), random.NextByte(), random.NextByte(), 255)
                );
            textures.Add(texture);
        }

        Material blitMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);
        TextureAtlasPacker packer = RenderingSystem.CreateTextureAtlasPacker(blitMaterial);
        for (int i = 0; i < spriteCount; i++)
        {
            packer.AddTexture($"sprite_{i}", textures[i]);
        }
        _atlas = packer.BuildTextureAtlas();

        _camera = RenderingSystem.CreateCamera2D(MainView.Size, 1000);
        _materialRenderer = RenderingSystem.CreateRenderContext();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer("_camera", _camera);
        _material.SetRenderTexture("_texture", _atlas.RenderTexture);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }


        Transform2D transform = Transform2D.Identity;
        transform.Scale = new Vector2(_atlas.RenderTexture.Width, _atlas.RenderTexture.Height);

        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        //draw atlas texture
        _materialRenderer.Begin(MainFrameBuffer);
        _materialRenderer.DrawWithConstant(RenderingSystem.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();

    }

    protected override void OnBeginFrame()
    {
        _mainPipeline.BeginFrame();
    }

    protected override void OnEndFrame()
    {
        _mainPipeline.RenderFrame(MainPresenter.FrameBuffer);
    }
}