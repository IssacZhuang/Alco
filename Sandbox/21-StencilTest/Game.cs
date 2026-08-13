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

        Bloom bloom = RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11);
        _mainPipeline.Use(new RGNode_Bloom(RenderingSystem, _mainPipeline.Graph, _mainPipeline.Chain, _mainPipeline.PostProcessLayout, bloom, BuiltInAssets.Shader_Blit));

        _mainPipeline.Use(new RGNode_Tonemap(
            RenderingSystem,
            _mainPipeline.Graph,
            _mainPipeline.Chain,
            _mainPipeline.PostProcessLayout,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap));

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        _shader = AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);

        // _camera = new CameraDataPerspective(1.03f, 0.1f, 1000, 16f / 9);
        // _camaraChild.position.Z = -10;
        // _camera.tranform = math.transform(_camaraParent, _camaraChild);

        _camera = RenderingSystem.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);
        _camaraChild.Position.X = -10;
        _camera.Transform = math.transform(_camaraParent, _camaraChild);

        _renderer = RenderingSystem.CreateRenderContext();
        _materialStencilWrite = RenderingSystem.CreateMaterial(_shader, "Unlit");
        _materialStencilWrite.SetBuffer("_camera", _camera);
        _materialStencilWrite.DepthStencilState = new DepthStencilState
        {
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.Always,
            FrontFace = StencilFaceState.Write,
            BackFace = StencilFaceState.Write,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };

        _materialStencilTest = RenderingSystem.CreateMaterial(_shader, "Unlit");
        _materialStencilTest.SetBuffer("_camera", _camera);
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

        if (Input.IsMousePressing(Mouse.Middle))
        {
            //_camaraParent.Rotate(Vector3.UnitY, Input.MouseDelta.Y * 0.01f);
            // _camaraParent.Rotate(Vector3.UnitZ, Input.MouseDelta.X * 0.01f);
            _rotationAngles += new Vector3(0, -Input.MouseDelta.Y , Input.MouseDelta.X );
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
