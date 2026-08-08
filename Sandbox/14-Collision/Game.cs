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

    private readonly ForwardPipeline _mainPipeline;

    private Plane3D _plane;
    private Vector3 offset;

    public ForwardPipeline MainPipeline => _mainPipeline;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

        _mainPipeline.Use(new SceneNode(this));

        Bloom bloom = RenderingSystem.CreateBloom(
            BuiltInAssets.Shader_BloomBlit,
            BuiltInAssets.Shader_BloomClamp,
            BuiltInAssets.Shader_BloomDownSample,
            BuiltInAssets.Shader_BloomUpSample,
            11);
        MainPipeline.Use(new RenderNode_Bloom(RenderingSystem, bloom, BuiltInAssets.Shader_Blit));

        var tonemapNode = new RenderNode_Tonemap(
            RenderingSystem,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        MainPipeline.Use(tonemapNode);

        _shader = AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Unlit);

        _camera = RenderingSystem.CreateCameraPerspective(1.03f, 16f / 9, 0.1f, 1000);

        _camera.Transform.Position.X = -10;
        _camera.UpdateMatrixToGPU();

        _renderer = RenderingSystem.CreateRenderContext();
        _material = RenderingSystem.CreateMaterial(_shader, "Unlit");

        _cameraBuffer = RenderingSystem.CreateGraphicsValueBuffer(_camera.Data.ViewProjectionMatrix, "camera_buffer");
        _material.SetBuffer("_camera", _cameraBuffer);

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
    private sealed class SceneNode : IForwardRenderNode
    {
        private readonly Game _game;

        public SceneNode(Game game)
        {
            _game = game;
        }

        public void OnRenderForward(GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            _game._renderer.Begin(target);
            _game._entity.OnDraw(_game._renderer);
            _game._renderer.End();
        }
    }

    private Cube CreateCube(ColorFloat color)
    {
        Cube ent = new Cube(RenderingSystem.MeshCube, _material);
        ent.Color = color;
        return ent;
    }
}