using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.FastRandom;
using Alco.Graphics;
using Alco.GUI;
using System.Diagnostics;
using Alco.ImGUI;

public class Game : GameEngine
{
    private static ColorFloat Color1 = new ColorFloat(0, 0, 0, 1f);
    private static ColorFloat Color2 = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    private static ColorFloat Color3 = new ColorFloat(1.25f, 2.5f, 1.25f, 1f);
    //scence
    private Transform3D _camaraParent = Transform3D.Identity;
    private Transform3D _camaraChild = Transform3D.Identity;

    private readonly ForwardPipeline _mainPipeline;

    private readonly Shader _shader;
    private readonly RenderContext _renderer;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly GraphicsMaterial _material;

    private readonly Cube _cube;

    private GizmoOperation _currentOperationEnum = GizmoOperation.Translate;

    private Vector3 _rotationAngles = Vector3.Zero;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(
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

        var tonemapNode = new RGNode_Tonemap(
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
            BuiltInAssets.Shader_AgXTonemap);
        _mainPipeline.Use(tonemapNode);

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        AddSystem(new ImGUISystem(this));

        _shader = AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);

        // _camera = new CameraDataPerspective(1.03f, 0.1f, 1000, 16f / 9);
        // _camaraChild.position.Z = -10;
        // _camera.tranform = math.transform(_camaraParent, _camaraChild);

        _camera = RenderingSystem.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);
        _camaraChild.Position.X = -10;
        _camera.Transform = math.transform(_camaraParent, _camaraChild);

        _renderer = RenderingSystem.CreateRenderContext();

        _material = RenderingSystem.CreateMaterial(_shader, "Unlit");
        _material.SetBuffer("_camera", _camera);

        _cube = new Cube(RenderingSystem.MeshCube, _material);
        _cube.Color = Color2;
        _cube.transform.Position = new Vector3(0, 0, 0);

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
            
            _rotationAngles += new Vector3(0, -Input.MouseDelta.Y, Input.MouseDelta.X);
        }

        _camera.Transform = math.transform(_camaraParent, _camaraChild);
        _camera.UpdateMatrixToGPU();

        ImGui.Begin("Transform");
        ImGui.Text("Hold mouse middle button to rotate camera");

        //zero allocation string build
        FixedString64 strMousePosition = new();
        strMousePosition.Append($"Mouse position: ");
        strMousePosition.Append(Input.MousePosition.X);
        strMousePosition.Append(", ");
        strMousePosition.Append(Input.MousePosition.Y);
        ImGui.Text(strMousePosition);

        ImGui.EditTransform3D(ref _cube.transform);
        ImGui.Combo("Operation", ref _currentOperationEnum);

        ImGui.End();

        Gizmo.Manipulate(_camera.Data.ViewMatrix, _camera.Data.ProjectionMatrix, _currentOperationEnum, GizmoMode.Local, ref _cube.transform);

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
    /// Content node drawing the cube into the pipeline-assigned target.
    /// </summary>
    private sealed class SceneNode : RGNode_SceneContent
    {
        private readonly Game _game;

        public SceneNode(Game game, RenderGraph graph, RenderChain chain) : base(graph, chain)
        {
            _game = game;
        }

        protected override void OnRender(GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            _game._renderer.Begin(target);
            _game._cube.OnDraw(_game._renderer);
            _game._renderer.End();
        }
    }
}
