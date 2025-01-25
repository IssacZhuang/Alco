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

    private readonly Material _surfaceMaterial;
    private readonly Material _cliffMaterial;
    private TileSet<int> _surfaceTileSet;
    private TileSet<int> _cliffTileSet;
    private readonly TiledTerrainBlock2D<int> _surfaceBlock;
    private readonly TiledTerrainBlock2D<int> _cliffBlock;
    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
    private ColorFloat _color = new ColorFloat(1, 1, 1, 1);

    private float _blendFactor = 0.2f;
    private float _edgeSmoothFactor = 0.1f;

    private uint _selectedTileId = 1;

    private float _hight = 0.2f;
    private float _brushSize = 1;
    private Material _brushMaterial;
    private Transform3D _brushTransform;
    private SpriteConstant _brushConstant;
    private List<int2> _brushCells = [];

    public Game(GameEngineSetting setting) : base(setting)
    {
        _blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);

        _surfaceTileSet = BuildSurfaceTileSet();
        _cliffTileSet = BuildCliffTileSet();

        float aspectRatio = MainWindow.Width / (float)MainWindow.Height;
 
        _camera = Rendering.CreateCamera2D(new Vector2(_zoom * aspectRatio, _zoom), 5);
        _renderer = Rendering.CreateMaterialRenderer();

        _surfaceMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TileSurface);
        _surfaceMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _surfaceMaterial.BlendState = BlendState.AlphaBlend;
        _surfaceMaterial.DepthStencilState = DepthStencilState.Write;
        _surfaceBlock = Rendering.CreateTiledTerrainBlock2D(_surfaceTileSet, _surfaceMaterial, 64, 64);

        _cliffMaterial = _surfaceMaterial.CreateInstance();
        _cliffMaterial.SetDefines("IS_CLIFF");

        _surfaceBlock.SetTilesId(1);
        _surfaceBlock.SetTilesColor(_color);

        _cliffBlock = Rendering.CreateTiledTerrainBlock2D(_cliffTileSet, _cliffMaterial, 64, 64);
        _cliffBlock.SetTilesId(1);
        _cliffBlock.SetTilesColor(new ColorFloat(0.9f, 0.9f, 0.9f, 1f));

        _brushMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        _brushMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _brushMaterial.SetTexture(ShaderResourceId.Texture, Rendering.TextureWhite);
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

        if (DebugGUI.SliderWithText("Selected Tile", ref _selectedTileId, 0, (uint)_surfaceTileSet.Count - 1))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Blend Width", ref _blendFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            _surfaceTileSet.BlendFactor = _blendFactor;
        }

        if (DebugGUI.SliderWithText("Height", ref _hight, -1f, 1f))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Edge Smooth", ref _edgeSmoothFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            _surfaceTileSet.EdgeSmoothFactor = _edgeSmoothFactor;
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
        _surfaceBlock.Render(_renderer);
        _cliffBlock.Render(_renderer);


        if (_surfaceBlock.TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
        {
            DebugGUI.Text($"Tile Position: {tilePosition}");
            Vector2 tileLocalPosition = _surfaceBlock.TilePositionToLocalPosition(tilePosition);
            DebugGUI.Text($"Tile Local Position: {tileLocalPosition}");


            for (int i = 0; i < _brushCells.Count; i++)
            {
                if (isDebugClicked)
                {
                    continue;
                }
                int2 pos = _brushCells[i];
                float height = _surfaceBlock.GetTileHeight(tilePosition.x + pos.x, tilePosition.y - pos.y);
                _brushTransform.position = new Vector3(pos.x + tileLocalPosition.X, pos.y + tileLocalPosition.Y + height, 0);
                Transform3D tmp = math.transform(_surfaceBlock.Transform, _brushTransform);
                _brushConstant.Model = tmp.Matrix;
                _renderer.DrawWithConstant(Rendering.MeshSprite, _brushMaterial, _brushConstant);


                if (Input.IsMousePressing(Mouse.Left))
                {
                    _surfaceBlock.SetTileId(tilePosition.x + pos.x, tilePosition.y + pos.y, _selectedTileId);
                    _cliffBlock.SetTileId(tilePosition.x + pos.x, tilePosition.y + pos.y, _selectedTileId);
                }
                else if (Input.IsMousePressing(Mouse.Right))
                {
                    _surfaceBlock.SetTileHeight(tilePosition.x + pos.x, tilePosition.y + pos.y, _hight);
                    _cliffBlock.SetTileHeight(tilePosition.x + pos.x, tilePosition.y + pos.y, _hight);
                }
            }
        }
        _renderer.End();
    }

    private void OnHotReload(string filename, object cachedAsset)
    {
        if (_surfaceTileSet.Atlas.TryGetSprite(filename, out Sprite? sprite))
        {
            _surfaceTileSet = BuildSurfaceTileSet();
            _surfaceBlock.UnsafeSetTileSet(_surfaceTileSet);
        }
    }

    private TileSet<int> BuildSurfaceTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsyncTask<Texture2D>("Textures/Grass.png");
        Task<Texture2D> sand = Assets.LoadAsyncTask<Texture2D>("Textures/Dirt.png");

        Task.WaitAll(grid, grass, sand);

        TileSetParams<int> tileSetParams = new();
        tileSetParams.HeightOffsetFactor = Vector2.UnitY;
        tileSetParams.BlendFactor = _blendFactor;
        tileSetParams.EdgeSmoothFactor = _edgeSmoothFactor;
        tileSetParams.Add(grid.Result, 0, Vector2.One, Vector2.One, 0.0f);
        tileSetParams.Add(grass.Result, 1, Vector2.One, Vector2.One, 1.0f);
        tileSetParams.Add(sand.Result, 2, Vector2.One, Vector2.One, 2.0f);
        return Rendering.CreateTileSet(_blitMaterial, tileSetParams, FilterMode.Nearest, "tile_set");
    }

    private TileSet<int> BuildCliffTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsyncTask<Texture2D>("Textures/GrassCliff.png");
        Task<Texture2D> sand = Assets.LoadAsyncTask<Texture2D>("Textures/DirtCliff.png");

        Task.WaitAll(grid, grass, sand);

        TileSetParams<int> tileSetParams = new();
        tileSetParams.HeightOffsetFactor = Vector2.UnitY;
        tileSetParams.BlendFactor = _blendFactor;
        tileSetParams.EdgeSmoothFactor = _edgeSmoothFactor;

        tileSetParams.Add(grid.Result, 0, Vector2.One, Vector2.One, 0.0f);
        tileSetParams.Add(grass.Result, 1, Vector2.One, Vector2.One, 1.0f);
        tileSetParams.Add(sand.Result, 2, Vector2.One, Vector2.One, 2.0f);
        return Rendering.CreateTileSet(_blitMaterial, tileSetParams, FilterMode.Nearest, "tile_set");
    }
}