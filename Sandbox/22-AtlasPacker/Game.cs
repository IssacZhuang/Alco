using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;
using Vocore.GUI;

public class Game : GameEngine
{
    private readonly TextureAtlas _atlas;
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
    }

    protected override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }
    }
}