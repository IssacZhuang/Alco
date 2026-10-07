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
        Surface,
        Wall
    }

    private readonly RenderPipeline _mainPipeline;

    private readonly Camera2D _camera;
    private readonly GraphicsMaterial _blitMaterial;

    public RenderPipeline MainPipeline => _mainPipeline;

    private readonly GraphicsMaterial _surfaceMaterial;
    private readonly GraphicsMaterial _cliffMaterial;
    private readonly GraphicsMaterial _waterMaterial;
    private TileSet _surfaceTileSet;

    private GraphicsMaterial _wallMaterial;
    private readonly TileRenderer _surfaceBlock;

    private readonly LightingManager _lightingManager;
    private readonly WallManager _wallManager;

    private float _zoom = 4f;
    private float _targetZoom = 4f;
    private float _zoomVelocity = 0f;
    private ColorFloat _color = new ColorFloat(1, 1, 1, 1);


    private EditMode _editMode = EditMode.Surface;
    private int _surfaceTileId = 1;

    private float _hight = 0.2f;
    private float _brushSize = 0.3f;
    private GraphicsMaterial _brushMaterial;
    private Transform3D _brushTransform;
    private SpriteConstant _brushConstant;
    private List<int2> _brushCells = [];

    private readonly GraphicsMaterial _materialLightOverlay;
    private SpriteConstant _lightOverlayConstant;

    private Color32 _waterColor = new Color32(128, 161, 168, 100);

    private bool _isEditWindowOpen = true;

    // Raw relative mouse deltas are device counts (mickeys), not pixels: a typical
    // 800-1600 DPI mouse on a 96 DPI display emits ~10 counts per cursor pixel.
    // Dividing by half that (~5) doubles the camera speed relative to the old
    // pixel-tuned feel.
    private const float RawMouseCountScale = 5f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new RenderPipeline(RenderingSystem, new RenderPipeline.Descriptor
        {
            SceneLayout = RenderingSystem.PreferredHDRPass,
            BlitShader = BuiltInAssets.Shader_Blit,
            Width = MainView.Size.X,
            Height = MainView.Size.Y,
        });

        _mainPipeline.Use(new SceneNode(this, _mainPipeline.Graph, _mainPipeline.Chain));

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        AddSystem(new ImGUISystem(this));

        var tonemapNode = new RGNode_Tonemap(
            RenderingSystem,
            MainPipeline.Graph,
            MainPipeline.Chain,
            MainPipeline.PostProcessLayout,
            new RGNode_Tonemap.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_Blit,
                ReinhardShader = BuiltInAssets.Shader_ReinhardLuminanceTonemap,
                Uncharted2Shader = BuiltInAssets.Shader_Uncharted2Tonemap,
                FilmicShader = BuiltInAssets.Shader_FilmicTonemap,
                AcesShader = BuiltInAssets.Shader_AcesTonemap,
                NeutralShader = BuiltInAssets.Shader_NeutralTonemap,
                AgxShader = BuiltInAssets.Shader_AgxTonemap,
            });
        MainPipeline.Use(tonemapNode);

        // FXAA runs after tone mapping: its luma-based edge detection assumes
        // tone-mapped input.
        var fxaaNode = new RGNode_FXAA(
            RenderingSystem,
            MainPipeline.Graph,
            MainPipeline.Chain,
            MainPipeline.PostProcessLayout,
            new RGNode_FXAA.Descriptor
            {
                SceneCopyShader = BuiltInAssets.Shader_Blit,
                FxaaShader = RenderingSystem.ShaderSystem.GetShader("FXAA"),
            });
        MainPipeline.Use(fxaaNode);

        int width = 64;
        int height = 64;

        float aspectRatio = MainView.Width / (float)MainView.Height;
        _camera = new Camera2D()
        {
            Size = new Vector2(_zoom * aspectRatio, _zoom),
            Near = -5,
            Far = 5
        };

        _camera.Transform.Position = new Vector2(width / 2, height / 2);
       
        RenderingSystem.MainCamera = _camera;

        _blitMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite, "sprite", "false");

        _lightingManager = new LightingManager(this, width, height);
        _wallManager = new WallManager(this, _lightingManager, width, height);

        _lightingManager.AddLight(new Light(new Vector2(width / 2, height / 2), new ColorFloat(1, 1, 1, 1)));
        _lightingManager.AddLight(new Light(new Vector2(0, 0), new ColorFloat(1, 1, 1, 1)));
        _lightingManager.SetLightMapDirty();
        _lightingManager.SetOpacityMapDirty();

        // Facade/bombing are value specializations: VertexMain<let IsFacade> / PixelMain<let Bombing>;
        // each material binds one combination.
        _surfaceMaterial = RenderingSystem.CreateGraphicsMaterial(
            RenderingSystem.ShaderSystem.GetShader("TileInstanced"), "tile_surface", false, false);
        _surfaceMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        _surfaceMaterial.DepthStencilState = DepthStencilState.Write;

        _cliffMaterial = RenderingSystem.CreateGraphicsMaterial(
            RenderingSystem.ShaderSystem.GetShader("TileInstanced"), "tile_cliff", true, false);
        _cliffMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        _cliffMaterial.DepthStencilState = DepthStencilState.Write;

        _waterMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_TileWaterInstanced);
        _waterMaterial.BlendState = BlendState.AlphaBlend;
        _waterMaterial.DepthStencilState = DepthStencilState.Read;

        _surfaceTileSet = BuildSurfaceTileSet();
        _surfaceBlock = RenderingSystem.CreateTileRenderer(_mainPipeline.Graph.RenderContext, _surfaceTileSet, width, height, "surface_block");
        _surfaceBlock.SetAllTiles(1);



        _brushMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite, "sprite", "false");
        _brushMaterial.SetTexture(ShaderResourceId.Texture, RenderingSystem.TextureWhite);
        _brushMaterial.BlendState = BlendState.NonPremultipliedAlpha;

        Texture2D textureWall = AssetSystem.Load<Texture2D>("Textures/Wall.png");

        _wallMaterial = RenderingSystem.CreateGraphicsMaterial(
            RenderingSystem.ShaderSystem.GetShader("tile-connectable"));
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

        GridUtility.FillCellsInRadius(_brushCells, _brushSize);

        _materialLightOverlay = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite, "sprite", "false");
        _materialLightOverlay.SetRenderTexture(ShaderResourceId.Texture, _lightingManager.LightMap);
        _materialLightOverlay.BlendState = BlendState.Multiply;

        Transform2D lightOverlayTransform = new Transform2D();
        lightOverlayTransform.Position = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);
        lightOverlayTransform.Scale = new Vector2(width, -height);
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
        yield return new DirectoryWatcherFileSource(Utils.GetRenderingAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), AssetSystem);
    }

    protected override void OnUpdate(float delta)
    {
        ImGui.Begin("Edit", ref _isEditWindowOpen);
        if (ImGui.SliderFloat("Brush Size", ref _brushSize, 0.1f, 5f))
        {
            GridUtility.FillCellsInRadius(_brushCells, _brushSize);
        }

        if (ImGui.SliderInt("Surface Tile", ref _surfaceTileId, 0, _surfaceTileSet.Count - 1))
        {

        }

        if (ImGui.Combo("Edit Mode", ref _editMode))
        {

        }

        for (int i = 0; i < _surfaceTileSet.Count; i++)
        {
            TileItem item = _surfaceTileSet.GetItem(i);
            float blendFactor = item.BlendFactor;
            if (ImGui.SliderFloat($"Blend Factor {i}", ref blendFactor, 0.01f, 0.5f))
            {
                item.BlendFactor = blendFactor;
            }
        }

        if (ImGui.SliderFloat("Height", ref _hight, -1f, 1f))
        {
        }


        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Pan with relative (raw) mouse input while the middle button is held:
        // the cursor hides during the drag and is restored where the drag started.
        bool panning = MainView.IsFocused && Input.IsMousePressing(Mouse.Middle);
        Input.IsMouseRelativeMode = panning;
        if (panning)
        {
            float speed = _zoom / MainView.Height;
            Vector2 mouseDelta = Input.MouseDelta / RawMouseCountScale;
            _camera.Transform.Position += new Vector2(-mouseDelta.X * speed, mouseDelta.Y * speed);
        }

        float wheelDelta = Input.MouseWheelDelta.Y;
        if (wheelDelta != 0)
        {
            _targetZoom -= wheelDelta * 0.5f;
            _targetZoom = math.clamp(_targetZoom, 2, 20);
        }

        Ray3D cameraRay = CameraMathUtility.ScreenPointToRay2D(MainView.MousePosition, MainView.Size, _camera.ViewProjectionMatrix, -100, 100);

        _zoom = math.damp(_zoom, _targetZoom, ref _zoomVelocity, 0.1f, 1000, delta);
        _camera.Size = new Vector2(_zoom * MainView.AspectRatio, _zoom);

        // Render lighting using internal command buffer
        _lightingManager.Render();

        ImGuiIOPtr io = ImGui.GetIO();

        if (TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
        {

            ImGui.Text($"Tile Position: {tilePosition}");

            for (int i = 0; i < _brushCells.Count; i++)
            {
                if (io.WantCaptureMouse)
                {
                    continue;
                }
                int2 pos = _brushCells[i] + tilePosition;

                if (Input.IsMousePressing(Mouse.Left))
                {
                    if (_editMode == EditMode.Surface)
                    {
                        _surfaceBlock.SetTile(pos.X, pos.Y, _surfaceTileId);
                    }
                    else if (_editMode == EditMode.Wall)
                    {
                        _wallManager.AddWall(new Wall(pos, _wallMaterial, new Vector2(1, 1.5f), new Vector2(0, 0.25f), new ColorFloat(0, 0, 0, 1f)));
                    }
                }

            }
        }

        if (MainPipeline.Get<RGNode_FXAA>() is { } fxaaNode)
        {
            bool isFXAAEnabled = fxaaNode.IsEnabled;
            if (ImGui.Checkbox("FXAA", ref isFXAAEnabled))
            {
                fxaaNode.IsEnabled = isFXAAEnabled;
            }
        }


        ImGui.End();

        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected override void OnStop()
    {
        _mainPipeline?.Dispose();
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

    private bool TryGetTilePositionByRay(Ray3D ray, out int2 tilePosition)
    {
        Matrix4x4 matrix = _surfaceBlock.Transform.Matrix;
        //to local space
        if (Matrix4x4.Invert(matrix, out Matrix4x4 invMatrix))
        {
            Vector3 start = ray.Origin;
            Vector3 end = ray.Origin + ray.Displacement;

            Vector3 localStart = Vector3.Transform(start, invMatrix);
            Vector3 localEnd = Vector3.Transform(end, invMatrix);

            Plane3D plane = new Plane3D(Vector3.UnitZ, 0);

            Ray3D localRay = new Ray3D(localStart, localEnd - localStart);

            if (plane.IntersectRay(localRay, out Vector3 hitPoint))
            {
                // TileRenderer Transform corresponds to bottom-left corner (0,0)
                // No offset needed since Transform is already at the correct position
                int tileX = (int)math.round(hitPoint.X);
                int tileY = (int)math.round(hitPoint.Y);

                int2 size = _surfaceBlock.Size;
                if (tileX >= 0 && tileX < size.X && tileY >= 0 && tileY < size.Y)
                {
                    tilePosition = new int2(tileX, tileY);
                    return true;
                }
            }
        }

        tilePosition = new int2(0, 0);
        return false;
    }

    private TileSet BuildSurfaceTileSet()
    {
        Task<Texture2D> grid = AssetSystem.LoadAsync<Texture2D>("Textures/Grid.png");
        Task<Texture2D> grass = AssetSystem.LoadAsync<Texture2D>("Textures/Grass.png");
        Task<Texture2D> grass2 = AssetSystem.LoadAsync<Texture2D>("Textures/Grass2.png");
        Task<Texture2D> grass3 = AssetSystem.LoadAsync<Texture2D>("Textures/Grass3.png");
        Task<Texture2D> grass4 = AssetSystem.LoadAsync<Texture2D>("Textures/Grass4.png");
        Task<Texture2D> sand = AssetSystem.LoadAsync<Texture2D>("Textures/Dirt.png");

        Task.WaitAll(grid, grass, sand);

        List<TileItem> items = new();

        GraphicsMaterial gridMaterial = _surfaceMaterial.CreateInstance();
        gridMaterial.SetTexture(ShaderResourceId.Texture, grid.Result);
        var item1 = new TileItem("grid", gridMaterial, 0, null);

        GraphicsMaterial grassMaterial = RenderingSystem.CreateGraphicsMaterial(
            RenderingSystem.ShaderSystem.GetShader("TileInstanced"), "tile_grass", false, true);
        grassMaterial.SetTexture(ShaderResourceId.Texture, grass.Result);
        var item2 = new TileItem("grass", grassMaterial, 1, null);

        GraphicsMaterial sandMaterial = RenderingSystem.CreateGraphicsMaterial(
            RenderingSystem.ShaderSystem.GetShader("TileInstanced"), "tile_sand", false, true);
        sandMaterial.SetTexture(ShaderResourceId.Texture, sand.Result);
        var item3 = new TileItem("sand", sandMaterial, 2, null);

        GraphicsMaterial waterMaterial = _waterMaterial.CreateInstance();
        waterMaterial.SetTexture(ShaderResourceId.Texture, RenderingSystem.TextureWhite);
        var item4 = new TileItem("water", waterMaterial, 1, null);
        item4.Color = new ColorFloat(0.15f, 0.54f, 0.67f, 0.8f);
        item4.BlendFactor = 0.35f;

        items.Add(item1);
        items.Add(item2);
        items.Add(item3);
        items.Add(item4);

        return new TileSet(items.ToArray());
    }

    private sealed class SceneNode : RGNode_SceneContent
    {
        private readonly Game _game;

        public SceneNode(Game game, RenderGraph graph, RenderChain chain) : base(graph, chain)
        {
            _game = game;
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                _game._surfaceBlock.Render();
                _game._wallManager.Render(pass);

                pass.DrawWithConstant(_game.RenderingSystem.MeshCenteredSprite, _game._materialLightOverlay, _game._lightOverlayConstant);

                Ray3D cameraRay = CameraMathUtility.ScreenPointToRay2D(_game.MainView.MousePosition, _game.MainView.Size, _game._camera.ViewProjectionMatrix, -100, 100);

                ImGuiIOPtr io = ImGui.GetIO();

                if (_game.TryGetTilePositionByRay(cameraRay, out int2 tilePosition))
                {
                    for (int i = 0; i < _game._brushCells.Count; i++)
                    {
                        if (io.WantCaptureMouse)
                        {
                            continue;
                        }
                        int2 pos = _game._brushCells[i] + tilePosition;

                        _game._brushTransform.Position = new Vector3(pos.X, pos.Y, 0);
                        Transform3D tmp = math.transform(_game._surfaceBlock.Transform, _game._brushTransform);
                        _game._brushConstant.Model = tmp.Matrix;
                        pass.DrawWithConstant(_game.RenderingSystem.MeshCenteredSprite, _game._brushMaterial, _game._brushConstant);
                    }
                }
            }
        }
    }
}