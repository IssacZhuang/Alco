using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.GUI;
using Alco.Graphics;
using Alco.IO;

using SandboxUtils;

public class Game : GameEngine
{
    private readonly MaterialRenderer _renderer;
    private readonly Camera2D _camera;
    private readonly Material _blitMaterial;

    private readonly Material _surfaceMaterial;
    private readonly Material _cliffMaterial;
    private readonly Material _waterMaterial;
    private readonly Material _plantMaterial;
    private SurfaceTileSet<int> _surfaceTileSet;
    private SurfaceTileSet<int> _cliffTileSet;
    private WaterTileSet<int> _waterTileSet;
    private PlantTileSet<int> _plantTileSet;
    private readonly SurfaceTileBlock2D<int> _surfaceBlock;
    private readonly SurfaceTileBlock2D<int> _cliffBlock;
    private readonly WaterTileBlock2D<int> _waterBlock;
    private readonly PlantTileBlock2D<int> _plantBlock;
    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
    private ColorFloat _color = new ColorFloat(1, 1, 1, 1);

    private float _blendFactor = 0.35f;
    private float _edgeSmoothFactor = 0.15f;

    private bool _editWater = false;
    private bool _editPlant = false;
    private uint _surfaceTileId = 1;
    private uint _waterTileId = 1;

    private float _hight = 0.2f;
    private float _brushSize = 1;
    private Material _brushMaterial;
    private Transform3D _brushTransform;
    private SpriteConstant _brushConstant;
    private List<int2> _brushCells = [];

    private Color32 _waterColor = new Color32(128, 161, 168, 100);

    public Game(GameEngineSetting setting) : base(setting)
    {
        int width = 64;
        int height = 64;

        _blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);

        float aspectRatio = MainWindow.Width / (float)MainWindow.Height;
 
        _camera = Rendering.CreateCamera2D(new Vector2(_zoom * aspectRatio, _zoom), 5);
        _renderer = Rendering.CreateMaterialRenderer();

        _surfaceTileSet = BuildSurfaceTileSet();
        _surfaceTileSet.SetAllTileColor(_color);
        _cliffTileSet = BuildCliffTileSet();
        _cliffTileSet.SetAllTileColor(new Vector4(0.9f, 0.9f, 0.9f, 1f));
        _waterTileSet = BuildWaterTileSet();
        _plantTileSet = BuildPlantTileSet();

        _surfaceMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TileSurface);
        _surfaceMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _surfaceMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        _surfaceMaterial.DepthStencilState = DepthStencilState.Write;
       
        _cliffMaterial = _surfaceMaterial.CreateInstance();

        _waterMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TileWater);
        _waterMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _waterMaterial.BlendState = BlendState.AlphaBlend;
        _waterMaterial.DepthStencilState = DepthStencilState.Read;

        _plantMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_TilePlant);
        _plantMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _plantMaterial.BlendState = BlendState.Opaque;
        _plantMaterial.DepthStencilState = DepthStencilState.Write;

        _surfaceBlock = Rendering.CreateSurfaceBlock2D(_surfaceTileSet, _surfaceMaterial, width, height);
        _surfaceBlock.SetAllItemIds(1);

        _cliffBlock = Rendering.CreateSurfaceBlock2D(_cliffTileSet, _cliffMaterial, width, height);
        _cliffBlock.SetAllItemIds(1);
        _cliffBlock.IsCliff = true;

        _waterBlock = Rendering.CreateWaterTileBlock2D(_waterTileSet, _waterMaterial, width, height);
        _waterBlock.SetAllItemIds(1);
        _waterBlock.Transform.position = new Vector3(0, -0.1f, -0.1f);
        _waterBlock.SurfaceHeightData = _surfaceBlock.HeightData;

        _plantBlock = Rendering.CreatePlantTileBlock2D(_plantTileSet, _plantMaterial, width, height);
        _plantBlock.SetAllItemIds(1);
        _plantBlock.Transform.position = new Vector3(0, 0, 0);

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

        if (DebugGUI.SliderWithText("Surface Tile", ref _surfaceTileId, 0, (uint)_surfaceTileSet.ItemCount - 1))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Water Tile", ref _waterTileId, 0, (uint)_waterTileSet.ItemCount - 1))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.CheckBoxWithText("Edit Water", ref _editWater))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Blend Width", ref _blendFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            for (uint i = 0; i < _surfaceTileSet.ItemCount; i++)
            {
                _surfaceTileSet.SetTileBlendFactor(i, _blendFactor);
            }
        }

        if (DebugGUI.SliderWithText("Height", ref _hight, -1f, 1f))
        {
            isDebugClicked = true;
        }

        if (DebugGUI.SliderWithText("Edge Smooth", ref _edgeSmoothFactor, 0.01f, 0.5f))
        {
            isDebugClicked = true;
            for (uint i = 0; i < _surfaceTileSet.ItemCount; i++)
            {
                _surfaceTileSet.SetTileEdgeSmoothFactor(i, _edgeSmoothFactor);
            }
        }

        // uint r = _waterColor.R;
        // uint g = _waterColor.G;
        // uint b = _waterColor.B;
        // uint a = _waterColor.A;
        // if (DebugGUI.SliderWithText("Water Color R", ref r, 0, 255))
        // {
        //     isDebugClicked = true;
        //     _waterColor.R = (byte)r;
        //     _waterTileSet.SetTileColor(1, _waterColor);
        // }

        // if (DebugGUI.SliderWithText("Water Color G", ref g, 0, 255))
        // {
        //     isDebugClicked = true;
        //     _waterColor.G = (byte)g;
        //     _waterTileSet.SetTileColor(1, _waterColor);
        // }

        // if (DebugGUI.SliderWithText("Water Color B", ref b, 0, 255))
        // {
        //     isDebugClicked = true;
        //     _waterColor.B = (byte)b;
        //     _waterTileSet.SetTileColor(1, _waterColor);
        // }

        // if (DebugGUI.SliderWithText("Water Color A", ref a, 0, 255))
        // {
        //     isDebugClicked = true;
        //     _waterColor.A = (byte)a;
        //     _waterTileSet.SetTileColor(1, _waterColor);
        // }

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
        _surfaceBlock.OnRender(_renderer);
        _cliffBlock.OnRender(_renderer);
        //_plantBlock.OnRender(_renderer);
        _waterBlock.OnRender(_renderer);


        if (_surfaceBlock.TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
        {
            //DebugGUI.Text($"Tile Position: {tilePosition}");
            Vector2 tileLocalPosition = _surfaceBlock.TilePositionToLocalPosition(tilePosition);
            //DebugGUI.Text($"Tile Local Position: {tileLocalPosition}");


            for (int i = 0; i < _brushCells.Count; i++)
            {
                if (isDebugClicked)
                {
                    continue;
                }
                int2 pos = _brushCells[i];
                if (!_surfaceBlock.TryGetTileHeight(tilePosition.x + pos.x, tilePosition.y - pos.y, out float height))
                {
                    continue;
                }
                _brushTransform.position = new Vector3(pos.x + tileLocalPosition.X, pos.y + tileLocalPosition.Y + height, 0);
                Transform3D tmp = math.transform(_surfaceBlock.Transform, _brushTransform);
                _brushConstant.Model = tmp.Matrix;
                _renderer.DrawWithConstant(Rendering.MeshSprite, _brushMaterial, _brushConstant);


                if (Input.IsMousePressing(Mouse.Left))
                {
                    if (_editWater)
                    {
                        _waterBlock.TrySetItemId(tilePosition.x + pos.x, tilePosition.y + pos.y, _waterTileId);
                    }
                    else
                    {
                        _surfaceBlock.TrySetItemId(tilePosition.x + pos.x, tilePosition.y + pos.y, _surfaceTileId);
                        _cliffBlock.TrySetItemId(tilePosition.x + pos.x, tilePosition.y + pos.y, _surfaceTileId);
                    }
                }
                else if (Input.IsMousePressing(Mouse.Right))
                {
                    _surfaceBlock.TrySetTileHeight(tilePosition.x + pos.x, tilePosition.y + pos.y, _hight);
                    _cliffBlock.TrySetTileHeight(tilePosition.x + pos.x, tilePosition.y + pos.y, _hight);
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

    private SurfaceTileSet<int> BuildSurfaceTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsyncTask<Texture2D>("Textures/Grass.png");
        Task<Texture2D> sand = Assets.LoadAsyncTask<Texture2D>("Textures/Dirt.png");

        Task.WaitAll(grid, grass, sand);

        List<SurfaceTileItem<int>> items = new();
        var item1 = new SurfaceTileItem<int>("grid", new SurfaceTileData(){
            BlendPriority = 0,
        }, 0);
        item1.AddTexture(grid.Result, 0);

        var item2 = new SurfaceTileItem<int>("grass", new SurfaceTileData(){
            BlendPriority = 1,
        }, 1);
        item2.AddTexture(grass.Result, 1);


        var item3 = new SurfaceTileItem<int>("sand", new SurfaceTileData(){
            BlendPriority = 2,
        }, 2);
        item3.AddTexture(sand.Result, 2);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);
        return Rendering.CreateSurfaceTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }

    private SurfaceTileSet<int> BuildCliffTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = Assets.LoadAsyncTask<Texture2D>("Textures/GrassCliff.png");
        Task<Texture2D> sand = Assets.LoadAsyncTask<Texture2D>("Textures/DirtCliff.png");

        Task.WaitAll(grid, grass, sand);

        List<SurfaceTileItem<int>> items = new();
        var item1 = new SurfaceTileItem<int>("grid", new SurfaceTileData(){
            BlendPriority = 0,
        }, 0);
        item1.AddTexture(grid.Result, 0);

        var item2 = new SurfaceTileItem<int>("grass", new SurfaceTileData(){
            BlendPriority = 1,
        }, 1);
        item2.AddTexture(grass.Result, 1);

        var item3 = new SurfaceTileItem<int>("sand", new SurfaceTileData(){
            BlendPriority = 2,
        }, 2);
        item3.AddTexture(sand.Result, 2);

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);

        return Rendering.CreateSurfaceTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }

    private WaterTileSet<int> BuildWaterTileSet()
    {
        Task<Texture2D> grid = Assets.LoadAsyncTask<Texture2D>("Textures/Grid.png");
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

        List<BaseTileItem<WaterTileData, int>> items = new();
        var item1 = new BaseTileItem<WaterTileData, int>("grid", new WaterTileData()
        {
            BlendPriority = 0
        }, 0);
        item1.AddTexture(grid.Result, 0);
        items.Add(item1);

        var item2 = new BaseTileItem<WaterTileData, int>("water", new WaterTileData()
        {
            Color = _waterColor,
            BlendPriority = 1
        }, 1);
        item2.AddTexture(Rendering.TextureWhite, 1);
        items.Add(item2);

        var item3 = new BaseTileItem<WaterTileData, int>("water2", new WaterTileData()
        {
            Color = new ColorFloat(1, 1, 1, 0.5f),
            BlendPriority = 2
        }, 2);
        item3.AddTexture(Rendering.TextureWhite, 2);
        items.Add(item3);

        return Rendering.CreateWaterTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }


    private PlantTileSet<int> BuildPlantTileSet()
    {
        Task<Texture2D> highGrass1 = Assets.LoadAsyncTask<Texture2D>("Textures/HighGrass1.png");
        Task<Texture2D> highGrass2 = Assets.LoadAsyncTask<Texture2D>("Textures/HighGrass2.png");
        Task.WaitAll(highGrass1, highGrass2);
        // PlantTileSetParams<int> tileSetParams = new();
        // tileSetParams.Add(highGrass1.Result, 0, new PlantTileData()
        // {
            
        // });
        // tileSetParams.Add(highGrass2.Result, 1, new PlantTileData()
        // {
            
        // });

        List<BaseTileItem<PlantTileData, int>> items = new();
        var item1 = new BaseTileItem<PlantTileData, int>("highGrass1", new PlantTileData()
        {
        }, 0);
        item1.AddTexture(highGrass1.Result, 0);
        items.Add(item1);

        var item2 = new BaseTileItem<PlantTileData, int>("highGrass2", new PlantTileData()
        {
        }, 1);
        item2.AddTexture(highGrass2.Result, 1);
        items.Add(item2);

        return Rendering.CreatePlantTileSet(_blitMaterial, items, FilterMode.Nearest, "tile_set");
    }
}