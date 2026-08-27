using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.FastRandom;
using Alco.Graphics;
using Alco.GUI;
using System.Diagnostics;

public class Game : GameEngine
{
    private static ColorFloat Color1 = new ColorFloat(0, 0, 0, 1f);
    private static ColorFloat Color2 = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    private static ColorFloat Color3 = new ColorFloat(1.25f, 2.5f, 1.25f, 1f);
    //scence
    private Transform3D _camaraParent = Transform3D.Identity;
    private Transform3D _camaraChild = Transform3D.Identity;

    private readonly Shader _shader;
    private readonly RenderContext _renderer;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly GraphicsMaterial _materialStencilWrite;
    private readonly GraphicsMaterial _materialStencilTest;

    private readonly Cube _cubeStencilWrite;
    private readonly Cube _cubeStencilTest1;
    private readonly Cube _cubeStencilTest2;

    private readonly RenderPipeline _mainPipeline;

    private Vector3 _rotationAngles = Vector3.Zero;

    // Raw relative mouse deltas are device counts (mickeys), not pixels: a typical
    // 800-1600 DPI mouse on a 96 DPI display emits ~10 counts per cursor pixel.
    // Dividing by half that (~5) doubles the camera speed relative to the old
    // pixel-tuned feel.
    private const float RawMouseCountScale = 5f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new RenderPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);
        _mainPipeline.ClearColor = new ColorFloat(0.2f, 0.2f, 0.2f, 1);

        // The node chain: scene content first, then bloom, then tone mapping.
        _mainPipeline.Use(new SceneNode(this, _mainPipeline.Graph, _mainPipeline.Chain));

        _mainPipeline.Use(new RGNode_Bloom(
            RenderingSystem,
            _mainPipeline.Graph,
            _mainPipeline.Chain,
            _mainPipeline.PostProcessLayout,
            new RGNode_Bloom.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_BloomBlit,
                ClampShader = BuiltInAssets.Shader_BloomClamp,
                DownsampleShader = BuiltInAssets.Shader_BloomDownsample,
                UpsampleShader = BuiltInAssets.Shader_BloomUpsample,
                TargetDownsampleHeight = 11,
                SceneCopyShader = BuiltInAssets.Shader_Blit,
            }));

        _mainPipeline.Use(new RGNode_Tonemap(
            RenderingSystem,
            _mainPipeline.Graph,
            _mainPipeline.Chain,
            _mainPipeline.PostProcessLayout,
            new RGNode_Tonemap.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_Blit,
                ReinhardShader = BuiltInAssets.Shader_ReinhardLuminanceTonemap,
                Uncharted2Shader = BuiltInAssets.Shader_Uncharted2Tonemap,
                FilmicShader = BuiltInAssets.Shader_FilmicTonemap,
                AcesShader = BuiltInAssets.Shader_AcesTonemap,
                NeutralShader = BuiltInAssets.Shader_NeutralTonemap,
                AgxShader = BuiltInAssets.Shader_AgxTonemap,
            }));

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        _shader = BuiltInAssets.Shader_Unlit;

        // _camera = new CameraDataPerspective(1.03f, 0.1f, 1000, 16f / 9);
        // _camaraChild.position.Z = -10;
        // _camera.tranform = math.transform(_camaraParent, _camaraChild);

        _camera = RenderingSystem.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);
        _camaraChild.Position.X = -10;
        _camera.Transform = math.transform(_camaraParent, _camaraChild);

        _renderer = RenderingSystem.CreateRenderContext();
        _materialStencilWrite = RenderingSystem.CreateGraphicsMaterial(_shader, "Unlit");
        _materialStencilWrite.SetBuffer("camera", _camera);
        _materialStencilWrite.DepthStencilState = new DepthStencilState
        {
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.Always,
            FrontFace = StencilFaceState.Write,
            BackFace = StencilFaceState.Write,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        _materialStencilTest = RenderingSystem.CreateGraphicsMaterial(_shader, "Unlit");
        _materialStencilTest.SetBuffer("camera", _camera);
        _materialStencilTest.DepthStencilState = new DepthStencilState
        {
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.LessEqual,
            FrontFace = StencilFaceState.CompareEqual,
            BackFace = StencilFaceState.CompareEqual,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        _cubeStencilWrite = new Cube(RenderingSystem.MeshCube, _materialStencilWrite);
        _cubeStencilWrite.Color = Color1;
        _cubeStencilWrite.transform.Position = new Vector3(0, 0, 0);
        _cubeStencilWrite.transform.Scale = new Vector3(0.1f, 5f, 5f);

        _cubeStencilTest1 = new Cube(RenderingSystem.MeshCube, _materialStencilTest);
        _cubeStencilTest1.Color = Color2;
        _cubeStencilTest1.transform.Position = new Vector3(2, 3f, 0);

        _cubeStencilTest2 = new Cube(RenderingSystem.MeshCube, _materialStencilTest);
        _cubeStencilTest2.Color = Color3;
        _cubeStencilTest2.transform.Position = new Vector3(1, -2f, 1);

        MainView.OnResize += OnMainWindowResize;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _camaraParent.Rotation = math.quaternion(_rotationAngles);

        // Rotate with relative (raw) mouse input while the middle button is held:
        // the cursor hides during the drag and is restored where the drag started.
        bool rotating = MainView.IsFocused && Input.IsMousePressing(Mouse.Middle);
        Input.IsMouseRelativeMode = rotating;
        if (rotating)
        {
            Vector2 mouseDelta = Input.MouseDelta / RawMouseCountScale;
            _rotationAngles += new Vector3(0, -mouseDelta.Y, mouseDelta.X);
        }

        _camera.Transform = math.transform(_camaraParent, _camaraChild);
        _camera.UpdateMatrixToGPU();

        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
    }

    protected override void OnStop()
    {
        _mainPipeline.Dispose();
    }

    /// <summary>
    /// Content node drawing the stencil test cubes into the pipeline-assigned target.
    /// </summary>
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
                pass.SetStencilReference(250);
                _game._cubeStencilWrite.OnDraw(pass);
                _game._cubeStencilTest1.OnDraw(pass);
                _game._cubeStencilTest2.OnDraw(pass);
            }
        }
    }
}
