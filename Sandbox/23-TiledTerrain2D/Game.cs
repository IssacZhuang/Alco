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
    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _atlasMaterial;
    private readonly Material _terrainMaterial;
    private readonly TiledTerrainBlock2D _terrainBlock;
    public Game(GameEngineSetting setting) : base(setting)
    {
        List<Texture2D> textures = [Assets.Load<Texture2D>("Grass.png"), Assets.Load<Texture2D>("Sand.png")];

        Material blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        TextureAtlasPacker packer = Rendering.CreateTextureAtlasPacker(blitMaterial,128,128);
        for (int i = 0; i < textures.Count; i++)
        {
            packer.AddTexture($"sprite_{i}", textures[i]);
        }
        _atlas = packer.BuildTextureAtlas();

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
        _atlasMaterial = blitMaterial.CreateInstance();
        _atlasMaterial.SetBuffer("_camera", _camera);
        _atlasMaterial.SetRenderTexture("_texture", _atlas.RenderTexture);

        _terrainMaterial = blitMaterial.CreateInstance();
        _terrainMaterial.SetBuffer("_camera", _camera);
        _terrainMaterial.SetRenderTexture("_texture", _atlas.RenderTexture);
        _terrainBlock = Rendering.CreateTiledTerrainBlock2D(_atlas, _terrainMaterial, 32, 32);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }


        // Transform2D transform = Transform2D.Identity;
        // transform.scale = new Vector2(_atlas.RenderTexture.Width, _atlas.RenderTexture.Height);

        // SpriteConstant constant = new SpriteConstant
        // {
        //     Model = transform.Matrix,
        //     Color = new ColorFloat(1, 1, 1, 1),
        //     UvRect = new Rect(0, 0, 1, 1)
        // };

        // //draw atlas texture
        // _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        // _materialRenderer.DrawWithConstant(Rendering.MeshSprite, _material, constant);
        // _materialRenderer.End();

    }
}