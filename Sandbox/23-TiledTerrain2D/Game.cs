using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.GUI;
using Vocore.Graphics;
using Vocore.IO;

using SandboxUtils;

public class Game : GameEngine
{
    private readonly MaterialRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Material _blitMaterial;
    private readonly Material _terrainMaterial;
    private TileSet<int> _tileSet;
    private readonly TiledTerrainBlock2D<int> _terrainBlock;
    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
    private ColorFloat _color = new ColorFloat(1, 1, 1, 1);

    private float _blendFactor = 0.1f;

    private uint _selectedTileId = 1;

    private float _hight = 0f;
    private float _brushSize = 1;
    private Material _brushMaterial;
    private Transform3D _brushTransform;
    private SpriteConstant _brushConstant;
    private List<int2> _brushCells = [];

    public Game(GameEngineSetting setting) : base(setting)
    {
        _blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);

        _tileSet = BuildTileSet();

        float aspectRatio = MainWindow.Width / (float)MainWindow.Height;
 
        _camera = Rendering.CreateCamera2D(new Vector2(_zoom * aspectRatio, _zoom), 5);
        _renderer = Rendering.CreateMaterialRenderer();

        _terrainMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TiledTerrain);
        _terrainMaterial.SetBuffer("_camera", _camera);
        _terrainMaterial.BlendState = BlendState.AlphaBlend;
        _terrainMaterial.DepthStencilState = DepthStencilState.Write;
        _terrainBlock = Rendering.CreateTiledTerrainBlock2D(_tileSet, _terrainMaterial, 31, 31);

        _terrainBlock.SetTilesId(1);
        _terrainBlock.SetTilesColor(_color);

        _brushMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        _brushMaterial.SetBuffer("_camera", _camera);
        _brushMaterial.SetTexture("_texture", Rendering.TextureWhite);
        _brushMaterial.BlendState = BlendState.NonPremultipliedAlpha;

        _brushTransform = new Transform3D();
        _brushTransform.scale = new Vector3(0.8f);
        _brushConstant = new SpriteConstant
        {
            Color = new ColorFloat(1, 1, 1, 0.3f),
            UvRect = new Rect(0, 0, 1, 1)
        };

        UtilsGrid.GetCellsInRadius(_brushCells, _brushSize);

        Assets.OnHotReload += OnHotReload;
    }

    protected override void InitializeDefaultAssetLoader(GameEngineSetting setting)
    {
        base.InitializeDefaultAssetLoader(setting);
        DirectoryWatcherFileSource fileSource1 = new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), Assets);
        Assets.AddFileSource(fileSource1);
        DirectoryWatcherFileSource fileSource2 = new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), Assets);
        Assets.AddFileSource(fileSource2);
    }

    protected override void OnUpdate(float delta)
    {
        DebugGUI.Text(FrameRate);
        bool isDebugClicked = false;
        if (DebugGUI.SliderWithText("Brush Size", ref _brushSize, 0.1f, 5f))
        {
            UtilsGrid.GetCellsInRadius(_brushCells, _brushSize);
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Selected Tile", ref _selectedTileId, 0, (uint)_tileSet.Count - 1))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Blend Width", ref _blendFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            _tileSet.BlendFactor = _blendFactor;
        }

        if (DebugGUI.SliderWithText("Height", ref _hight, -1f, 1f))
        {
            isDebugClicked = true;
        }

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

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay2D(MainWindow.MousePosition, MainWindow.Size, _camera.Data.ViewProjectionMatrix, -100, 100);

        _zoom = math.damp(_zoom, _targetZoom, ref _zoomVelocity, 0.1f, 1000, delta);
        _camera.Width = _zoom * MainWindow.AspectRatio; 
        _camera.Height = _zoom;

        _camera.UpdateMatrixToGPU();



        _renderer.Begin(MainRenderTarget.FrameBuffer);
        _terrainBlock.Render(_renderer);

        if (_terrainBlock.TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
        {
            DebugGUI.Text($"Tile Position: {tilePosition}");
            Vector2 tileLocalPosition = _terrainBlock.TilePositionToLocalPosition(tilePosition);
            DebugGUI.Text($"Tile Local Position: {tileLocalPosition}");


            for (int i = 0; i < _brushCells.Count; i++)
            {
                if (isDebugClicked)
                {
                    continue;
                }
                int2 pos = _brushCells[i];
                _brushTransform.position = new Vector3(pos.x + tileLocalPosition.X, pos.y + tileLocalPosition.Y, 0);
                Transform3D tmp = math.transform(_terrainBlock.Transform, _brushTransform);
                _brushConstant.Model = tmp.Matrix;
                _renderer.DrawWithConstant(Rendering.MeshSprite, _brushMaterial, _brushConstant);


                if (Input.IsMousePressing(Mouse.Left))
                {
                    _terrainBlock.SetTileId(tilePosition.x + pos.x, tilePosition.y + pos.y, _selectedTileId);
                    
                }
                else if (Input.IsMousePressing(Mouse.Right))
                {
                    _terrainBlock.SetTileHeight(tilePosition.x + pos.x, tilePosition.y + pos.y, _hight);
                }
            }
        }
        _renderer.End();
    }

    private void OnHotReload(string filename, object cachedAsset)
    {
        if (_tileSet.Atlas.TryGetSprite(filename, out Sprite? sprite))
        {
            _tileSet = BuildTileSet();
            _terrainBlock.UnsafeSetTileSet(_tileSet);
        }
    }

    private TileSet<int> BuildTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsyncTask<Texture2D>("Textures/Grass.png");
        Task<Texture2D> sand = Assets.LoadAsyncTask<Texture2D>("Textures/Dirt.png");

        Task.WaitAll(grid, grass, sand);

        TileSetParams<int> tileSetParams = new();
        tileSetParams.HeightOffsetFactor = Vector2.UnitY;
        tileSetParams.BlendFactor = 0.2f;
        tileSetParams.EdgeSmoothFactor = 0.2f;
        tileSetParams.Add(grid.Result, 0, Vector2.One, Vector2.One, 0.0f);
        tileSetParams.Add(grass.Result, 1, Vector2.One, Vector2.One, 1.0f);
        tileSetParams.Add(sand.Result, 2, Vector2.One, Vector2.One, 2.0f);
        return Rendering.CreateTileSet(_blitMaterial, tileSetParams, FilterMode.Nearest, "tile_set");
    }
}