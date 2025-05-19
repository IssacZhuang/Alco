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
        Plant,
        Wall
    }

    private readonly RenderContext _renderer;
    private readonly Camera2D _camera;
    private readonly Material _blitMaterial;

    private readonly Material _surfaceMaterial;
    private readonly Material _cliffMaterial;
    private readonly Material _waterMaterial;
    private readonly Material _plantMaterial;
    private SurfaceTileSet _surfaceTileSet;
    private SurfaceTileSet _cliffTileSet;
    private WaterTileSet _waterTileSet;
    private PlantTileSet _plantTileSet;
    private ConnectableTileData _wallData;
    private readonly SurfaceTileBlock2D _surfaceBlock;
    private readonly SurfaceTileBlock2D _cliffBlock;
    private readonly WaterTileBlock2D _waterBlock;
    private readonly PlantTileBlock2D _plantBlock;
    private readonly TileMapHeightBuffer _heightBuffer;

    private readonly LightingManager _lightingManager;
    private readonly WallManager _wallManager;

    private readonly RenderTexture _blurTexture;
    private GaussianBlur _gaussianBlur;

    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
    private ColorFloat _color = new ColorFloat(1, 1, 1, 1);

    private float _blendFactor = 0.35f;
    private float _edgeSmoothFactor = 0.15f;

    private float _blurCenter = 4;
    private float _blurSide = 3;
    private float _blurCorner = 2;

    private EditMode _editMode = EditMode.Surface;
    private int _surfaceTileId = 1;
    private int _waterTileId = 1;
    private int _plantTileId = 0;

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
        _camera = Rendering.CreateCamera2D(new Vector2(_zoom * aspectRatio, _zoom), 5);
        Rendering.MainCamera = _camera;

        _blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);

        _renderer = Rendering.CreateRenderContext();

        _heightBuffer = Rendering.CreateTileMapHeightBuffer(width, height);

        ComputeMaterial computeMaterial = Rendering.CreateComputeMaterial(BuiltInAssets.Shader_TileLighting);
        computeMaterial.SetBuffer(ShaderResourceId.HeightData, _heightBuffer);

        _lightingManager = new LightingManager(this, _heightBuffer, width, height);
        _wallManager = new WallManager(this, _lightingManager, width, height);

        _lightingManager.AddLight(new Light(new Vector2(width / 2, height / 2), new ColorFloat(1, 1, 1, 1)));
        _lightingManager.AddLight(new Light(new Vector2(0, 0), new ColorFloat(1, 1, 1, 1)));
        _lightingManager.SetLightMapDirty();
        _lightingManager.SetOpacityMapDirty();

        _surfaceTileSet = BuildSurfaceTileSet();
        _surfaceTileSet.SetAllTileColor(_color);
        _cliffTileSet = BuildCliffTileSet();
        _cliffTileSet.SetAllTileColor(new Vector4(0.9f, 0.9f, 0.9f, 1f));
        _waterTileSet = BuildWaterTileSet();
        _plantTileSet = BuildPlantTileSet();

        _surfaceMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TileSurface);
        _surfaceMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        _surfaceMaterial.DepthStencilState = DepthStencilState.Write;
       
        _cliffMaterial = _surfaceMaterial.CreateInstance();

        _waterMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TileWater);
        _waterMaterial.BlendState = BlendState.AlphaBlend;
        _waterMaterial.DepthStencilState = DepthStencilState.Read;

        _plantMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TilePlant);
        _plantMaterial.BlendState = BlendState.Opaque;
        _plantMaterial.DepthStencilState = DepthStencilState.Write;

        _surfaceBlock = Rendering.CreateSurfaceBlock2D(_surfaceTileSet, _heightBuffer, _surfaceMaterial, width, height);
        // _surfaceBlock.UseLightMap = true;
        _surfaceBlock.SetAllItemIds(1);

        _cliffBlock = Rendering.CreateSurfaceBlock2D(_cliffTileSet, _heightBuffer, _cliffMaterial, width, height);
        _cliffBlock.SetAllItemIds(1);
        _cliffBlock.IsCliff = true;


        _waterBlock = Rendering.CreateWaterTileBlock2D(_waterTileSet, _heightBuffer, _waterMaterial, width, height);
        _waterBlock.SetAllItemIds(1);
        _waterBlock.Transform.Position = new Vector3(0, -0.1f, 0.1f);


        _plantBlock = Rendering.CreatePlantTileBlock2D(_plantTileSet, _heightBuffer, _plantMaterial, width, height);
        _plantBlock.SetAllItemIds(0);
        _plantBlock.Transform.Position = new Vector3(0, 0, 0);


        _brushMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        _brushMaterial.SetTexture(ShaderResourceId.Texture, Rendering.TextureWhite);
        _brushMaterial.BlendState = BlendState.NonPremultipliedAlpha;

        Texture2D textureWall = Assets.Load<Texture2D>("Textures/Wall.png");
        textureWall.SetSampler(GraphicsDevice.SamplerNearestClamp);

        Material material = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TileConnectable);
        material.BlendState = BlendState.Opaque;
        material.DepthStencilState = DepthStencilState.Write;
        material.SetTexture(ShaderResourceId.Texture, textureWall);

        _wallData = new ConnectableTileData(material, new Vector2(1, 1.5f), new Vector2(0, 0.25f), new ColorFloat(0, 0, 0, 1f), null);


        _brushTransform = new Transform3D();
        _brushTransform.Scale = new Vector3(0.8f);
        _brushConstant = new SpriteConstant
        {
            Color = new ColorFloat(1, 1, 1, 0.3f),
            UvRect = new Rect(0, 0, 1, 1)
        };

        ComputeMaterial gaussianBlurMaterial = Rendering.CreateComputeMaterial(BuiltInAssets.Shader_GaussianBlurRGBA16F);
        _blurTexture = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, (uint)width, (uint)height);

        _gaussianBlur = Rendering.CreateGaussianBlur(gaussianBlurMaterial, 3, 3, [
            _blurCorner, _blurSide, _blurCorner,
            _blurSide, _blurCenter, _blurSide,
            _blurCorner, _blurSide, _blurCorner
        ]);

        UtilsGrid.GetCellsInRadius(_brushCells, _brushSize);

        _materialLightOverlay = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        _materialLightOverlay.SetRenderTexture(ShaderResourceId.Texture, _blurTexture);
        _materialLightOverlay.BlendState = BlendState.Multiply;

        Transform2D lightOverlayTransform = new Transform2D();
        lightOverlayTransform.Scale = new Vector2(width, height);
        _lightOverlayConstant = new SpriteConstant()
        {
            Color = new ColorFloat(1, 1, 1, 0.5f),
            UvRect = new Rect(0, 0, 1, 1),
            Model = lightOverlayTransform.Matrix
        };

        Assets.OnHotReload += OnHotReload;
    }

    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), Assets);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), Assets);
    }

    protected override void OnUpdate(float delta)
    {
        DebugGUI.Text(FrameRate);
        bool isDebugClicked = false;

        ImGui.Begin("Edit", ref _isEditWindowOpen);
        if (ImGui.SliderFloat("Brush Size", ref _brushSize, 0.1f, 5f))
        {
            UtilsGrid.GetCellsInRadius(_brushCells, _brushSize);
            isDebugClicked = true;
        }

        if (ImGui.SliderInt("Surface Tile", ref _surfaceTileId, 0, _surfaceTileSet.ItemCount - 1))
        {
            isDebugClicked = true;
        }

        if (ImGui.SliderInt("Water Tile", ref _waterTileId, 0, _waterTileSet.ItemCount - 1))
        {
            isDebugClicked = true;
        }

        if (ImGui.SliderInt("Plant Tile", ref _plantTileId, 0, _plantTileSet.ItemCount - 1))
        {
            isDebugClicked = true;
        }

        if (ImGui.Combo("Edit Mode", ref _editMode))
        {
            isDebugClicked = true;
        }


        if (ImGui.SliderFloat("Blend Width", ref _blendFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            for (uint i = 0; i < _surfaceTileSet.ItemCount; i++)
            {
                _surfaceTileSet.SetTileBlendFactor(i, _blendFactor);
            }
        }

        if (ImGui.SliderFloat("Height", ref _hight, -1f, 1f))
        {
            isDebugClicked = true;
        }

        if (ImGui.SliderFloat("Edge Smooth", ref _edgeSmoothFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            for (uint i = 0; i < _surfaceTileSet.ItemCount; i++)
            {
                _surfaceTileSet.SetTileEdgeSmoothFactor(i, _edgeSmoothFactor);
            }
        }

        if (ImGui.SliderFloat("Blur Center", ref _blurCenter, 1, 10) ||
            ImGui.SliderFloat("Blur Side", ref _blurSide, 1, 5) ||
            ImGui.SliderFloat("Blur Corner", ref _blurCorner, 0, 3))
        {
            _gaussianBlur.SetKernel([
                _blurCorner, _blurSide, _blurCorner,
                _blurSide, _blurCenter, _blurSide,
                _blurCorner, _blurSide, _blurCorner
            ]);
            isDebugClicked = true;
        }

        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsMousePressing(Mouse.Middle))
        {
            float speed = _zoom / MainView.Height;
            _camera.Position += new Vector2(-Input.MouseDelta.X * speed, Input.MouseDelta.Y * speed);
        }

        if (Input.IsMouseWheelScrolling(out float wheelDelta))
        {
            _targetZoom -= wheelDelta * 0.5f;
            _targetZoom = math.clamp(_targetZoom, 2, 20);
        }

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay2D(MainView.MousePosition, MainView.Size, _camera.Data.ViewProjectionMatrix, -100, 100);

        _zoom = math.damp(_zoom, _targetZoom, ref _zoomVelocity, 0.1f, 1000, delta);
        _camera.Width = _zoom * MainView.AspectRatio;
        _camera.Height = _zoom;

        _camera.UpdateMatrixToGPU();

        // _lightingManager.SetLightMapDirty();
        // _lightingManager.SetOpacityMapDirty();

        _lightingManager.Render();
        _gaussianBlur.Blit(_lightingManager.LightMap, _blurTexture);

        _renderer.Begin(MainRenderTarget.FrameBuffer);
        _surfaceBlock.OnRender(_renderer);
        _cliffBlock.OnRender(_renderer);
        _plantBlock.OnRender(_renderer);
        _waterBlock.OnRender(_renderer);
        _wallManager.Render(_renderer);

        _renderer.DrawWithConstant(Rendering.MeshCenteredSprite, _materialLightOverlay, _lightOverlayConstant);


        if (_surfaceBlock.TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
        {
            //DebugGUI.Text($"Tile Position: {tilePosition}");
            Vector2 tileLocalPosition = _surfaceBlock.TilePositionToLocalPosition(tilePosition);
            //DebugGUI.Text($"Tile Local Position: {tileLocalPosition}");

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
                _brushTransform.Position = new Vector3(pos.X + tileLocalPosition.X, pos.Y + tileLocalPosition.Y + height, 0);
                Transform3D tmp = math.transform(_surfaceBlock.Transform, _brushTransform);
                _brushConstant.Model = tmp.Matrix;
                _renderer.DrawWithConstant(Rendering.MeshCenteredSprite, _brushMaterial, _brushConstant);


                if (Input.IsMousePressing(Mouse.Left))
                {
                    if (_editMode == EditMode.Water)
                    {
                        _waterBlock.TrySetItemId(tilePosition.X + pos.X, tilePosition.Y + pos.Y, (uint)_waterTileId);
                    }
                    else if (_editMode == EditMode.Surface)
                    {
                        _surfaceBlock.TrySetItemId(tilePosition.X + pos.X, tilePosition.Y + pos.Y, (uint)_surfaceTileId);
                        _cliffBlock.TrySetItemId(tilePosition.X + pos.X, tilePosition.Y + pos.Y, (uint)_surfaceTileId);
                    }
                    else if (_editMode == EditMode.Plant)
                    {
                        _plantBlock.TrySetItemId(tilePosition.X + pos.X, tilePosition.Y + pos.Y, (uint)_plantTileId);
                    }
                    else if (_editMode == EditMode.Wall)
                    {
                        _wallManager.AddWall(new Wall(tilePosition, _wallData));
                    }
                }
                else if (Input.IsMousePressing(Mouse.Right))
                {
                    _heightBuffer.TrySetTileHeight(tilePosition.X + pos.X, tilePosition.Y + pos.Y, _hight);
                }

            }
        }
        _renderer.End();

        ImGui.End();
    }

    private void OnHotReload(string filename, object cachedAsset)
    {
        if (_surfaceTileSet.Atlas.TryGetSprite(filename, out Sprite? sprite))
        {
            _surfaceTileSet = BuildSurfaceTileSet();
            _surfaceBlock.UnsafeSetTileSet(_surfaceTileSet);
        }
    }

    private SurfaceTileSet BuildSurfaceTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsync<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsync<Texture2D>("Textures/Grass.png");
        Task<Texture2D> grass2 = Assets.LoadAsync<Texture2D>("Textures/Grass2.png");
        Task<Texture2D> grass3 = Assets.LoadAsync<Texture2D>("Textures/Grass3.png");
        Task<Texture2D> grass4 = Assets.LoadAsync<Texture2D>("Textures/Grass4.png");
        Task<Texture2D> sand = Assets.LoadAsync<Texture2D>("Textures/Dirt.png");


        Task.WaitAll(grid, grass, sand);

        List<SurfaceTileItem> items = new();
        var item1 = new SurfaceTileItem("grid", new SurfaceTileData(){
            BlendPriority = 0,
        }, 0, grid.Result);


        var item2 = new SurfaceTileItem("grass", new SurfaceTileData(){
            BlendPriority = 1,
        }, 1, grass.Result, grass2.Result, grass3.Result, grass4.Result);


        var item3 = new SurfaceTileItem("sand", new SurfaceTileData(){
            BlendPriority = 2,
        }, 2, sand.Result);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);
        return Rendering.CreateSurfaceTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }

    private SurfaceTileSet BuildCliffTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsync<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsync<Texture2D>("Textures/GrassCliff.png");
        Task<Texture2D> sand = Assets.LoadAsync<Texture2D>("Textures/DirtCliff.png");

        Task.WaitAll(grid, grass, sand);

        List<SurfaceTileItem> items = new();
        var item1 = new SurfaceTileItem("grid", new SurfaceTileData(){
            BlendPriority = 0,
        }, 0, grid.Result);

        var item2 = new SurfaceTileItem("grass", new SurfaceTileData(){
            BlendPriority = 1,
        }, 1, grass.Result);

        var item3 = new SurfaceTileItem("sand", new SurfaceTileData(){
            BlendPriority = 2,
        }, 2, sand.Result);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);

        return Rendering.CreateSurfaceTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }

    private WaterTileSet BuildWaterTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsync<Texture2D>("Textures/Grid.png");
        Task.WaitAll(grid);
        // WaterTileSetParams<int> tileSetParams = new();
        // tileSetParams.Add(grid.Result, 0, new WaterTileData()
        // {
        //     BlendPriority = 0
        // });
        // tileSetParams.Add(Rendering.TextureWhite, 0, new WaterTileData()
        // {
        //     Color = _waterColor,
        //     BlendPriority = 1
        // });
        // tileSetParams.Add(Rendering.TextureWhite, 0, new WaterTileData()
        // {
        //     Color = new ColorFloat(1, 1, 1, 0.5f),
        //     BlendPriority = 2
        // });

        List<WaterTileItem> items = new();
        var item1 = new WaterTileItem("grid", new WaterTileData()
        {
            BlendPriority = 0
        }, 0, grid.Result);
        items.Add(item1);

        var item2 = new WaterTileItem("water", new WaterTileData()
        {
            Color = _waterColor,
            BlendPriority = 1
        }, 1, Rendering.TextureWhite);
        items.Add(item2);

        var item3 = new WaterTileItem("water2", new WaterTileData()
        {
            Color = new ColorFloat(1, 1, 1, 0.5f),
            BlendPriority = 2
        }, 2, Rendering.TextureWhite);
        items.Add(item3);

        return Rendering.CreateWaterTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }


    private PlantTileSet BuildPlantTileSet()
    {
        Task<Texture2D> highGrass1 = Assets.LoadAsync<Texture2D>("Textures/HighGrass1.png");
        Task<Texture2D> highGrass2 = Assets.LoadAsync<Texture2D>("Textures/HighGrass2.png");
        Task.WaitAll(highGrass1, highGrass2);
        // PlantTileSetParams<int> tileSetParams = new();
        // tileSetParams.Add(highGrass1.Result, 0, new PlantTileData()
        // {
            
        // });
        // tileSetParams.Add(highGrass2.Result, 1, new PlantTileData()
        // {
            
        // });

        List<PlantTileItem> items = new();
        //add empty item
        var item0 = new PlantTileItem("empty", new PlantTileData()
        {
            HasContent = 0
        }, 0, Rendering.TextureWhite);
        items.Add(item0);

        var item1 = new PlantTileItem("highGrass1", new PlantTileData()
        {
        }, 0, highGrass1.Result, highGrass2.Result);
        items.Add(item1);

        return Rendering.CreatePlantTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }
}