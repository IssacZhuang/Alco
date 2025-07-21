using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.GUI;
using Alco.Graphics;
using Alco.IO;

using SandboxUtils;
using Alco.ImGUI;

public class Game : GameEngine
{
    private enum EditMode
    {
        None,
        Water,
        Surface,
        Wall
    }

    private readonly RenderContext _renderer;
    private readonly Camera2D _camera;
    private readonly Material _blitMaterial;

    private readonly Material _surfaceMaterial;
    private readonly Material _cliffMaterial;
    private readonly Material _waterMaterial;
    private NewTileSet _surfaceTileSet;
    private NewTileSet _cliffTileSet;
    private NewTileSet _waterTileSet;
    private Material _wallMaterial;
    private readonly TileRenderer _surfaceBlock;
    private readonly TileRenderer _cliffBlock;
    private readonly TileRenderer _waterBlock;
    private readonly TileMapHeightBuffer _heightBuffer;

    private readonly LightingManager _lightingManager;
    private readonly WallManager _wallManager;

    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
    private ColorFloat _color = new ColorFloat(1, 1, 1, 1);


    private EditMode _editMode = EditMode.Surface;
    private int _surfaceTileId = 1;
    private int _waterTileId = 1;

    private float _hight = 0.2f;
    private float _brushSize = 0.3f;
    private Material _brushMaterial;
    private Transform3D _brushTransform;
    private SpriteConstant _brushConstant;
    private List<int2> _brushCells = [];

    private readonly Material _materialLightOverlay;
    private SpriteConstant _lightOverlayConstant;

    private Color32 _waterColor = new Color32(128, 161, 168, 100);

    private bool _isEditWindowOpen = true;

    public Game(GameEngineSetting setting) : base(setting)
    {
        int width = 64;
        int height = 64;

        float aspectRatio = MainView.Width / (float)MainView.Height;
        _camera = new Camera2D()
        {
            Size = new Vector2(_zoom * aspectRatio, _zoom),
            Near = -5,
            Far = 5
        };
       
        RenderingSystem.MainCamera = _camera;

        _blitMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);

        _renderer = RenderingSystem.CreateRenderContext();

        _heightBuffer = RenderingSystem.CreateTileMapHeightBuffer(width, height);

        ComputeMaterial computeMaterial = RenderingSystem.CreateComputeMaterial(BuiltInAssets.Shader_TileLighting);
        computeMaterial.SetBuffer(ShaderResourceId.HeightData, _heightBuffer);

        _lightingManager = new LightingManager(this, _heightBuffer, width, height);
        _wallManager = new WallManager(this, _lightingManager, width, height);

        _lightingManager.AddLight(new Light(new Vector2(width / 2, height / 2), new ColorFloat(1, 1, 1, 1)));
        _lightingManager.AddLight(new Light(new Vector2(0, 0), new ColorFloat(1, 1, 1, 1)));
        _lightingManager.SetLightMapDirty();
        _lightingManager.SetOpacityMapDirty();

        _surfaceMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_TileInstanced);
        _surfaceMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        _surfaceMaterial.DepthStencilState = DepthStencilState.Write;

        _cliffMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_TileInstanced);
        _cliffMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        _cliffMaterial.DepthStencilState = DepthStencilState.Write;
        _cliffMaterial.SetDefines("IS_FACADE");

        _waterMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_TileWaterInstanced);
        _waterMaterial.BlendState = BlendState.AlphaBlend;
        _waterMaterial.DepthStencilState = DepthStencilState.Read;

        _surfaceTileSet = BuildSurfaceTileSet();
        _cliffTileSet = BuildCliffTileSet();
        _waterTileSet = BuildWaterTileSet();

        _surfaceBlock = RenderingSystem.CreateTileRenderer(_renderer, _surfaceTileSet, width, height, "surface_block");
        _surfaceBlock.SetAllTiles(1);

        _cliffBlock = RenderingSystem.CreateTileRenderer(_renderer, _cliffTileSet, width, height, "cliff_block");
        _cliffBlock.SetAllTiles(1);


        _waterBlock = RenderingSystem.CreateTileRenderer(_renderer, _waterTileSet, width, height, "water_block");
        _waterBlock.SetAllTiles(1);
        _waterBlock.Transform.Position = new Vector3(0, -0.1f, 0.1f);


        _brushMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);
        _brushMaterial.SetTexture(ShaderResourceId.Texture, RenderingSystem.TextureWhite);
        _brushMaterial.BlendState = BlendState.NonPremultipliedAlpha;

        Texture2D textureWall = AssetSystem.Load<Texture2D>("Textures/Wall.png");

        _wallMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_TileConnectable);
        _wallMaterial.BlendState = BlendState.Opaque;
        _wallMaterial.DepthStencilState = DepthStencilState.Write;
        _wallMaterial.SetTexture(ShaderResourceId.Texture, textureWall);



        _brushTransform = new Transform3D();
        _brushTransform.Scale = new Vector3(0.8f);
        _brushConstant = new SpriteConstant
        {
            Color = new ColorFloat(1, 1, 1, 0.3f),
            UvRect = new Rect(0, 0, 1, 1)
        };

        UtilsGrid.FillCellsInRadius(_brushCells, _brushSize);

        _materialLightOverlay = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);
        _materialLightOverlay.SetRenderTexture(ShaderResourceId.Texture, _lightingManager.LightMap);
        _materialLightOverlay.BlendState = BlendState.Multiply;

        Transform2D lightOverlayTransform = new Transform2D();
        lightOverlayTransform.Position = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);
        lightOverlayTransform.Scale = new Vector2(width, height);
        _lightOverlayConstant = new SpriteConstant()
        {
            Color = new ColorFloat(1, 1, 1, 0.5f),
            UvRect = new Rect(0, 0, 1, 1),
            Model = lightOverlayTransform.Matrix
        };

        AssetSystem.OnHotReload += OnHotReload;
    }

    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), AssetSystem);
    }

    protected override void OnUpdate(float delta)
    {
        DebugStats.Text(FrameRate);
        bool isDebugClicked = false;

        ImGui.Begin("Edit", ref _isEditWindowOpen);
        if (ImGui.SliderFloat("Brush Size", ref _brushSize, 0.1f, 5f))
        {
            UtilsGrid.FillCellsInRadius(_brushCells, _brushSize);
            isDebugClicked = true;
        }

        if (ImGui.SliderInt("Surface Tile", ref _surfaceTileId, 0, _surfaceTileSet.Count - 1))
        {
            isDebugClicked = true;
        }

        if (ImGui.SliderInt("Water Tile", ref _waterTileId, 0, _waterTileSet.Count - 1))
        {
            isDebugClicked = true;
        }

        if (ImGui.Combo("Edit Mode", ref _editMode))
        {
            isDebugClicked = true;
        }


        // Note: Blend Width and Edge Smooth controls are not supported by NewTileSet
        // if (ImGui.SliderFloat("Blend Width", ref _blendFactor, 0.01f, 0.5f))
        // {
        //     isDebugClicked = true;
        // }

        if (ImGui.SliderFloat("Height", ref _hight, -1f, 1f))
        {
            isDebugClicked = true;
        }

        // if (ImGui.SliderFloat("Edge Smooth", ref _edgeSmoothFactor, 0.01f, 0.5f))
        // {
        //     isDebugClicked = true;
        // }

        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsMousePressing(Mouse.Middle))
        {
            float speed = _zoom / MainView.Height;
            _camera.Transform.Position += new Vector2(-Input.MouseDelta.X * speed, Input.MouseDelta.Y * speed);
        }

        if (Input.IsMouseWheelScrolling(out float wheelDelta))
        {
            _targetZoom -= wheelDelta * 0.5f;
            _targetZoom = math.clamp(_targetZoom, 2, 20);
        }

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay2D(MainView.MousePosition, MainView.Size, _camera.ViewProjectionMatrix, -100, 100);

        _zoom = math.damp(_zoom, _targetZoom, ref _zoomVelocity, 0.1f, 1000, delta);
        _camera.Size = new Vector2(_zoom * MainView.AspectRatio, _zoom);

        // Render lighting using internal command buffer
        _lightingManager.Render();

        _renderer.Begin(MainRenderTarget.FrameBuffer);
        _surfaceBlock.Render();
        _cliffBlock.Render();
        _waterBlock.Render();
        _wallManager.Render(_renderer);

        _renderer.DrawWithConstant(RenderingSystem.MeshCenteredSprite, _materialLightOverlay, _lightOverlayConstant);


        if (_surfaceBlock.TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
        {

            ImGui.Text($"Tile Position: {tilePosition}");

            for (int i = 0; i < _brushCells.Count; i++)
            {
                if (isDebugClicked)
                {
                    continue;
                }
                int2 pos = _brushCells[i];
                if (!_heightBuffer.TryGetTileHeight(tilePosition.X + pos.X, tilePosition.Y - pos.Y, out float height))
                {
                    continue;
                }
                _brushTransform.Position = new Vector3(pos.X + tilePosition.X, pos.Y + tilePosition.Y + height, 0);
                Transform3D tmp = math.transform(_surfaceBlock.Transform, _brushTransform);
                _brushConstant.Model = tmp.Matrix;
                _renderer.DrawWithConstant(RenderingSystem.MeshCenteredSprite, _brushMaterial, _brushConstant);


                if (Input.IsMousePressing(Mouse.Left))
                {
                    if (_editMode == EditMode.Water)
                    {
                        _waterBlock.SetTile(tilePosition.X + pos.X, tilePosition.Y + pos.Y, _waterTileId);
                    }
                    else if (_editMode == EditMode.Surface)
                    {
                        _surfaceBlock.SetTile(tilePosition.X + pos.X, tilePosition.Y + pos.Y, _surfaceTileId);
                        _cliffBlock.SetTile(tilePosition.X + pos.X, tilePosition.Y + pos.Y, _surfaceTileId);
                    }
                    else if (_editMode == EditMode.Wall)
                    {
                        _wallManager.AddWall(new Wall(tilePosition, _wallMaterial, new Vector2(1, 1.5f), new Vector2(0, 0.25f), new ColorFloat(0, 0, 0, 1f)));
                    }
                }
                else if (Input.IsMousePressing(Mouse.Right))
                {
                    _heightBuffer.TrySetTileHeight(tilePosition.X + pos.X, tilePosition.Y + pos.Y, _hight);
                }

            }
        }
        _renderer.End();

        if (TryGetSystem<FXAASystem>(out var fxaaSystem))
        {
            bool isFXAAEnabled = fxaaSystem.IsEnabled;
            if (ImGui.Checkbox("FXAA", ref isFXAAEnabled))
            {
                fxaaSystem.IsEnabled = isFXAAEnabled;
            }
        }


        ImGui.End();
    }

    protected override void OnStop()
    {
        _lightingManager?.Dispose();
    }

    private void OnHotReload(string filename, object cachedAsset)
    {
        // Hot reload functionality simplified for NewTileSet
        // Original implementation relied on Atlas which is not available in NewTileSet
        if (filename.EndsWith(".png") || filename.EndsWith(".jpg"))
        {
            // Rebuild the tile set when textures are reloaded
            _surfaceTileSet = BuildSurfaceTileSet();
            // Note: TileRenderer doesn't support hot-swapping tile sets
            // A full recreation of the renderer would be needed
        }
    }

    private NewTileSet BuildSurfaceTileSet()
    {
        Task<Texture2D> grid = AssetSystem.LoadAsync<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = AssetSystem.LoadAsync<Texture2D>("Textures/Grass.png");
        Task<Texture2D> grass2 = AssetSystem.LoadAsync<Texture2D>("Textures/Grass2.png");
        Task<Texture2D> grass3 = AssetSystem.LoadAsync<Texture2D>("Textures/Grass3.png");
        Task<Texture2D> grass4 = AssetSystem.LoadAsync<Texture2D>("Textures/Grass4.png");
        Task<Texture2D> sand = AssetSystem.LoadAsync<Texture2D>("Textures/Dirt.png");

        Task.WaitAll(grid, grass, sand);

        List<NewTileSetitem> items = new();

        Material gridMaterial = _surfaceMaterial.CreateInstance();
        gridMaterial.SetTexture(ShaderResourceId.Texture, grid.Result);
        var item1 = new NewTileSetitem("grid", gridMaterial, 0, null);

        Material grassMaterial = _surfaceMaterial.CreateInstance();
        grassMaterial.SetTexture(ShaderResourceId.Texture, grass.Result);
        var item2 = new NewTileSetitem("grass", grassMaterial, 1, null);

        Material sandMaterial = _surfaceMaterial.CreateInstance();
        sandMaterial.SetTexture(ShaderResourceId.Texture, sand.Result);
        var item3 = new NewTileSetitem("sand", sandMaterial, 2, null);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);

        return new NewTileSet(items.ToArray());
    }

    private NewTileSet BuildCliffTileSet()
    {
        Task<Texture2D> grid = AssetSystem.LoadAsync<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = AssetSystem.LoadAsync<Texture2D>("Textures/GrassCliff.png");
        Task<Texture2D> sand = AssetSystem.LoadAsync<Texture2D>("Textures/DirtCliff.png");

        Task.WaitAll(grid, grass, sand);

        List<NewTileSetitem> items = new();

        Material gridMaterial = _cliffMaterial.CreateInstance();
        gridMaterial.SetTexture(ShaderResourceId.Texture, grid.Result);
        var item1 = new NewTileSetitem("grid", gridMaterial, 0, null);

        Material grassMaterial = _cliffMaterial.CreateInstance();
        grassMaterial.SetTexture(ShaderResourceId.Texture, grass.Result);
        var item2 = new NewTileSetitem("grass", grassMaterial, 1, null);

        Material sandMaterial = _cliffMaterial.CreateInstance();
        sandMaterial.SetTexture(ShaderResourceId.Texture, sand.Result);
        var item3 = new NewTileSetitem("sand", sandMaterial, 2, null);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);

        return new NewTileSet(items.ToArray());
    }

    private NewTileSet BuildWaterTileSet()
    {
        Task<Texture2D> grid = AssetSystem.LoadAsync<Texture2D>("Textures/Grid.png");
        Task.WaitAll(grid);

        List<NewTileSetitem> items = new();


        Material gridMaterial = _waterMaterial.CreateInstance();
        gridMaterial.SetTexture(ShaderResourceId.Texture, grid.Result);
        var item1 = new NewTileSetitem("grid", gridMaterial, 0, null);

        Material waterMaterial = _waterMaterial.CreateInstance();
        waterMaterial.SetTexture(ShaderResourceId.Texture, RenderingSystem.TextureWhite);
        var item2 = new NewTileSetitem("water", waterMaterial, 1, null);

        Material water2Material = _waterMaterial.CreateInstance();
        water2Material.SetTexture(ShaderResourceId.Texture, RenderingSystem.TextureWhite);
        var item3 = new NewTileSetitem("water2", water2Material, 2, null);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);

        return new NewTileSet(items.ToArray());
    }



}