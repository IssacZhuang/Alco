using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.GUI;

public class Game : GameEngine
{
    private readonly MaterialRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Material _terrainMaterial;
    private readonly TileSet<int> _tileSet;
    private readonly TiledTerrainBlock2D<int> _terrainBlock;
    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
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

        TileSetParams<int> tileSetParams = new();
        for (int i = 0; i < textures.Count; i++)
        {
            tileSetParams.Add(textures[i], i);
        }
        _tileSet = Rendering.CreateTileSet(blitMaterial, tileSetParams, "tile_set");

        float aspectRatio = MainWindow.Width / (float)MainWindow.Height;
 
        _camera = Rendering.CreateCamera2D(new Vector2(_zoom * aspectRatio, _zoom), 1000);
        _renderer = Rendering.CreateMaterialRenderer();

        _terrainMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TiledTerrain);
        _terrainMaterial.SetBuffer("_camera", _camera);
        _terrainBlock = Rendering.CreateTiledTerrainBlock2D(_tileSet, _terrainMaterial, 32, 32);

        _terrainBlock.SetTilesId(new int2(0, 0), new int2(32, 32), 1);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsMousePressing(Mouse.Middle))
        {
            float speed = _zoom / MainWindow.Height;
            _camera.Position += new Vector2(-Input.MouseDelta.X * speed, Input.MouseDelta.Y * speed);
        }

        if (Input.IsMouseWheelScrolling(out float wheelDelta))
        {
            _targetZoom -= wheelDelta * 0.5f;
            _targetZoom = math.clamp(_targetZoom, 2, 20);
        }

        _zoom = math.damp(_zoom, _targetZoom, ref _zoomVelocity, 0.1f, 1000, delta);
        _camera.Width = _zoom * MainWindow.AspectRatio; 
        _camera.Height = _zoom;

        _camera.UpdateMatrixToGPU();

        DebugGUI.Text(FrameRate);

        _renderer.Begin(MainRenderTarget.FrameBuffer);
        _terrainBlock.Render(_renderer);
        _renderer.End();
    }
}