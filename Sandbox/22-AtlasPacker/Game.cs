using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.Random;
using Alco.Graphics;
using Alco.GUI;

public class Game : GameEngine
{
    private readonly TextureAtlas _atlas;
    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    public Game(GameEngineSetting setting) : base(setting)
    {
        Random random = new Random(123456789);
        int spriteCount = 32;
        List<int2> spriteSizes = new List<int2>();
        List<Texture2D> textures = new List<Texture2D>();
        for (int i = 0; i < spriteCount; i++)
        {
            uint width = random.NextUint(1, 128);
            uint height = random.NextUint(1, 128);
            spriteSizes.Add(new int2(width, height));
            Texture2D texture = Rendering.CreateTexture2D(
                width, 
                height, 
                new Color32(random.NextByte(), random.NextByte(), random.NextByte(), 255)
                );
            textures.Add(texture);
        }

        Material blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        TextureAtlasPacker packer = Rendering.CreateTextureAtlasPacker(blitMaterial);
        for (int i = 0; i < spriteCount; i++)
        {
            packer.AddTexture($"sprite_{i}", textures[i]);
        }
        _atlas = packer.BuildTextureAtlas();

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
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
        transform.scale = new Vector2(_atlas.RenderTexture.Width, _atlas.RenderTexture.Height);

        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();

    }
}