using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.FastRandom;
using Alco.Graphics;
using Alco.GUI;
using Alco.ImGUI;

public class Game : GameEngine
{
    private static ColorFloat Color = new ColorFloat(1f, 0.5f, 0.5f, 1f);
    private static ColorFloat ColorHit = new ColorFloat(2.5f, 1.25f, 1.25f, 1f);
    //scence
    private readonly CameraPerspectiveBuffer _camera;

    private readonly Shader _shader;
    private readonly RenderContext _renderer;
    private readonly GraphicsMaterial _material;
    private readonly GraphicsValueBuffer<Matrix4x4> _cameraBuffer;

    private readonly Cube _entity;

    private readonly RenderPipeline _mainPipeline;

    private Plane3D _plane;
    private Vector3 offset;

    public RenderPipeline MainPipeline => _mainPipeline;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new RenderPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

        _mainPipeline.Use(new SceneNode(this, _mainPipeline.Graph, _mainPipeline.Chain));

        MainPipeline.Use(new RGNode_Bloom(
            RenderingSystem,
            MainPipeline.Graph,
            MainPipeline.Chain,
            MainPipeline.PostProcessLayout,
            new RGNode_Bloom.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_BloomBlit,
                ClampShader = BuiltInAssets.Shader_BloomClamp,
                DownsampleShader = BuiltInAssets.Shader_BloomDownsample,
                UpsampleShader = BuiltInAssets.Shader_BloomUpsample,
                TargetDownsampleHeight = 11,
                SceneCopyShader = BuiltInAssets.Shader_Blit,
            }));

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

        _shader = BuiltInAssets.Shader_Unlit;

        _camera = RenderingSystem.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);

        _camera.Transform.Position.X = -10;
        _camera.UpdateMatrixToGPU();

        _renderer = RenderingSystem.CreateRenderContext();
        _material = RenderingSystem.CreateGraphicsMaterial(_shader, "Unlit");

        _cameraBuffer = RenderingSystem.CreateGraphicsValueBuffer(_camera.Data.ViewProjectionMatrix, "camera_buffer");
        _material.SetBuffer("camera", _cameraBuffer);

        _plane = new Plane3D(new Vector3(1, 0, 0), 0);

        _entity = CreateCube(Color);
        _entity.transform.Position = new Vector3(2, 0, 0);
        _entity.transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 8);

        AddSystem(new ImGUISystem(this));

        MainView.OnResize += OnMainWindowResize;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }
        

        Vector2 localMousePosition = MainView.MousePosition;

        Ray3D cameraRay = _camera.Data.ScreenPointToRay(localMousePosition, MainView.Size) * 10;

        bool hit = CollisionUtility3D.RayBox(cameraRay * 10, _entity.Shape, out RaycastHit3D rayCastHit);

        _entity.Color = hit ? ColorHit : Color;

        _plane.IntersectRay(cameraRay, out Vector3 mouseWoldPosition);

        // ImGUI Controls
        ImGui.Begin("Collision Controls");

        // Display mouse information
        FixedString64 mouseLocalText = new FixedString64();
        mouseLocalText.Append("Local Mouse: ");
        mouseLocalText.Append(localMousePosition.X);
        mouseLocalText.Append(", ");
        mouseLocalText.Append(localMousePosition.Y);
        ImGui.Text(mouseLocalText);

        FixedString64 mouseWorldText = new FixedString64();
        mouseWorldText.Append("World Mouse: ");
        mouseWorldText.Append(mouseWoldPosition.X);
        mouseWorldText.Append(", ");
        mouseWorldText.Append(mouseWoldPosition.Y);
        mouseWorldText.Append(", ");
        mouseWorldText.Append(mouseWoldPosition.Z);
        ImGui.Text(mouseWorldText);

        if (Input.IsMouseDown(Mouse.Left) && hit)
        {
            offset = _entity.transform.Position - mouseWoldPosition;
        }

        // if (_isDragging)
        // {
        //     _entity.transform.Position = mouseWoldPosition + offset;
        // }

        if (Input.IsMouseUp(Mouse.Left))
        {
            
        }

        //camera controls
        ImGui.Separator();
        ImGui.Text("Camera Data");
        float fovFloat = _camera.FieldOfView * 100;
        if (ImGui.SliderFloat("Fov", ref fovFloat, 30, 110))
        {
            _camera.FieldOfView = fovFloat / 100f;
            _cameraBuffer.UpdateBuffer(_camera.Data.ViewProjectionMatrix);
        }

        ImGui.End();

        Gizmo.Manipulate(_camera.Data.ViewMatrix, _camera.Data.ProjectionMatrix, GizmoOperation.Translate, GizmoMode.Local, ref _entity.transform);

        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
        _camera.UpdateMatrixToGPU();
        _mainPipeline.Resize(size.X, size.Y);
    }

    protected override void OnStop()
    {
        _mainPipeline.Dispose();
    }

    /// <summary>
    /// Content node drawing the collision scene into the pipeline-assigned target.
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
                _game._entity.OnDraw(pass);
            }
        }
    }

    private Cube CreateCube(ColorFloat color)
    {
        Cube ent = new Cube(RenderingSystem.MeshCube, _material);
        ent.Color = color;
        return ent;
    }
}
