using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

public class Game : GameEngine
{
    private readonly TextureAtlas _atlas;
    private readonly MaterialRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Material _atlasMaterial;
    private readonly Material _terrainMaterial;
    private readonly TiledTerrainBlock2D _terrainBlock;
    private float _zoom = 4f;
    public Game(GameEngineSetting setting) : base(setting)
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsyncTask<Texture2D>("Textures/Grass.png");
        Task<Texture2D> sand = Assets.LoadAsyncTask<Texture2D>("Textures/Sand.png");
        
        Task.WaitAll(grid, grass, sand);

        List<Texture2D> textures = [
            grid.Result,
            grass.Result,
            sand.Result,
            ];

        Material blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        TextureAtlasPacker packer = Rendering.CreateTextureAtlasPacker(blitMaterial,128,128);
        for (int i = 0; i < textures.Count; i++)
        {
            packer.AddTexture($"sprite_{i}", textures[i]);
        }
        _atlas = packer.BuildTextureAtlas();

        float aspectRatio = MainWindow.Width / (float)MainWindow.Height;

        _camera = Rendering.CreateCamera2D(new Vector2(_zoom * aspectRatio, _zoom), 1000);
        _renderer = Rendering.CreateMaterialRenderer();
        _atlasMaterial = blitMaterial.CreateInstance();
        _atlasMaterial.SetBuffer("_camera", _camera);
        _atlasMaterial.SetRenderTexture("_texture", _atlas.RenderTexture);

        _terrainMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TiledTerrain);
        _terrainMaterial.SetBuffer("_camera", _camera);
        _terrainBlock = Rendering.CreateTiledTerrainBlock2D(_atlas, _terrainMaterial, 32, 32);

        MainWindow.OnResize += OnResize;
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
        // _renderer.Begin(MainRenderTarget.FrameBuffer);
        // _renderer.DrawWithConstant(Rendering.MeshSprite, _atlasMaterial, constant);
        // _renderer.End();

        _renderer.Begin(MainRenderTarget.FrameBuffer);
        _terrainBlock.Render(_renderer);
        _renderer.End();
    }

    private void OnResize(uint2 size)
    {
        float aspectRatio = size.x / (float)size.y;
        _camera.Width = _zoom * aspectRatio;
        _camera.Height = _zoom;
        _camera.UpdateMatrixToGPU();
    }
}