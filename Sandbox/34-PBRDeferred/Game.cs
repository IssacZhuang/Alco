using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.GUI;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;
using SandboxUtils;

/// <summary>
/// Sandbox demonstrating 3D PBR rendering with a deferred pipeline:
/// G-buffer pass, deferred lighting (GGX BRDF), a shadow-mapped directional
/// sun, up to four point lights and a procedural gradient skybox.
/// <br/>Controls: drag with the left mouse button to orbit the camera,
/// mouse wheel to zoom, ESC to exit.
/// </summary>
public class Game : GameEngine
{
    /// <summary>A PBR scene object: mesh, transform and surface parameters.</summary>
    private sealed class SceneObject
    {
        public required PrimitiveMesh Mesh;
        public Transform3D Transform = Transform3D.Identity;
        public Vector3 BaseColor;
        public float Metallic;
        public float Roughness;
        public float AmbientOcclusion = 1.0f;
        public bool CastsShadow = true;
        public float SpinSpeed;
        public float FloatSpeed;
        public float FloatPhase;

        public Matrix4x4 WorldMatrix => Transform.Matrix;
    }

    private readonly PBRDeferredPipeline _pipeline;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly PrimitiveMesh _cubeMesh;
    private readonly PrimitiveMesh _sphereMesh;
    private readonly PrimitiveMesh _groundMesh;
    private readonly Texture2D _checkerTexture;

    private readonly List<SceneObject> _objects = new();
    private PBRDeferredPipeline.DeferredLightingData _lightingData = new();

    // Camera orbit state.
    private float _yaw = 0.8f;
    private float _pitch = 0.35f;
    private float _distance = 15f;

    // Sun light.
    private Vector3 _sunDirection = Vector3.Normalize(new Vector3(-0.55f, -0.4f, -0.75f));
    private Vector3 _sunColor = new(1.0f, 0.95f, 0.85f);
    private float _sunIntensity = 8.0f;
    private bool _shadowEnabled = true;
    private bool _sunDiscEnabled = true;

    // Point lights (up to four).
    private readonly PBRDeferredPipeline.PointLight[] _pointLights = new PBRDeferredPipeline.PointLight[4];

    // Sky.
    private Vector3 _skyTopColor = new(0.10f, 0.20f, 0.42f);
    private Vector3 _skyBottomColor = new(0.52f, 0.60f, 0.70f);

    // Material tweak panel.
    private int _selectedObject;
    private bool _animateObjects = true;

    private float _time;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _checkerTexture = CreateCheckerTexture(256);

        _cubeMesh = CreateCubeMesh();
        _sphereMesh = CreateSphereMesh(48, 24);
        _groundMesh = CreateGroundMesh(40, 10);

        _camera = RenderingSystem.CreateCameraPerspective(0.83f, 16f / 9, 0.1f, 200);

        string lightingShaderText = AssetSystem.Load<string>(BuiltInAssetsPath.Shader_PBRDeferredLighting);
        _pipeline = new PBRDeferredPipeline(
            RenderingSystem,
            BuiltInAssets.Shader_PBRGBuffer,
            BuiltInAssets.Shader_PBRShadowDepth,
            lightingShaderText,
            BuiltInAssetsPath.Shader_PBRDeferredLighting,
            shadowMapSize: 2048,
            width: (uint)MainView.Size.X,
            height: (uint)MainView.Size.Y,
            albedoTexture: _checkerTexture);

        // The G-buffer pass needs the camera matrix; bind it explicitly like the
        // forward sandboxes do (RenderingSystem.MainCamera is not set by sandboxes).
        _pipeline.SetCamera(_camera);

        BuildScene();

        // Point light defaults: warm, cool, mouse-follow, disabled.
        _pointLights[0] = new PBRDeferredPipeline.PointLight(new Vector3(-6, -4, 2.0f), new Vector3(1.0f, 0.65f, 0.35f), 10.0f);
        _pointLights[1] = new PBRDeferredPipeline.PointLight(new Vector3(5, 3, 2.5f), new Vector3(0.35f, 0.5f, 1.0f), 8.0f);
        _pointLights[2] = new PBRDeferredPipeline.PointLight(Vector3.Zero, new Vector3(0.4f, 1.0f, 0.6f), 6.0f);
        _pointLights[3] = new PBRDeferredPipeline.PointLight(new Vector3(0, 6, 6.0f), new Vector3(1.0f, 1.0f, 1.0f), 0.0f);

        MainView.OnResize += OnMainWindowResize;
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

    protected override void OnStart()
    {
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _time += delta;

        UpdateCamera(delta);
        if (_animateObjects)
        {
            AnimateObjects(delta);
        }

        UpdateLightingData();

        RenderFrame();

        DrawImGuiPanel();

        DebugStats.Text(FrameRate);
        DebugStats.Text("LMB drag: orbit  |  wheel: zoom  |  ESC: exit");
    }

    protected override void OnStop()
    {
    }

    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
        _pipeline.Resize(size.X, size.Y);
    }

    private void UpdateCamera(float delta)
    {
        // Do not orbit/zoom while the mouse is over an ImGui window: dragging a
        // slider or scrolling the panel must not move the camera.
        bool mouseOverImGui = ImGUIInputHandler.IsCapturingMouse;

        if (!mouseOverImGui && Input.IsMousePressing(Mouse.Left))
        {
            Vector2 mouseDelta = Input.MouseDelta;
            _yaw -= mouseDelta.X * 0.008f;
            _pitch -= mouseDelta.Y * 0.008f;
        }

        if (!mouseOverImGui && Input.IsMouseScrolling(out Vector2 wheel))
        {
            _distance = Math.Clamp(_distance - wheel.Y * 0.8f, 4f, 60f);
        }

        // Keep the camera above the ground plane (z = 0): the pitch floor depends
        // on the orbit distance so the camera can still get low at far zoom but
        // never dips under the ground.
        float minPitch = MathF.Asin(0.5f / _distance);
        _pitch = Math.Clamp(_pitch, minPitch, 1.45f);

        Vector3 cameraDirection = new(
            MathF.Cos(_pitch) * MathF.Cos(_yaw),
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch));
        Vector3 cameraPosition = cameraDirection * _distance;
        Vector3 lookDirection = Vector3.Normalize(-cameraDirection);

        _camera.Transform = new Transform3D(cameraPosition, LookRotation(lookDirection, Vector3.UnitZ));
        _camera.UpdateMatrixToGPU();
    }

    private void AnimateObjects(float delta)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (sceneObject.SpinSpeed != 0)
            {
                Vector3 axis = i % 2 == 0 ? Vector3.UnitZ : Vector3.UnitX;
                sceneObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(axis, sceneObject.SpinSpeed * delta) * sceneObject.Transform.Rotation;
            }
            if (sceneObject.FloatSpeed != 0)
            {
                sceneObject.Transform.Position.Z = sceneObject.FloatPhase
                    + MathF.Sin(_time * sceneObject.FloatSpeed) * 0.6f;
            }
        }
    }

    private void UpdateLightingData()
    {
        Vector3 sunDirection = Vector3.Normalize(_sunDirection);

        // Build the sun's orthographic view-projection around the scene center.
        Vector3 center = Vector3.Zero;
        Vector3 eye = center - sunDirection * 60f;
        Vector3 up = Math.Abs(Vector3.Dot(sunDirection, Vector3.UnitZ)) > 0.95f ? Vector3.UnitY : Vector3.UnitZ;
        Matrix4x4 sunView = Matrix4x4.CreateLookAtLeftHanded(eye, center, up);
        Matrix4x4 sunProjection = Matrix4x4.CreateOrthographicLeftHanded(42f, 42f, 0.1f, 130f);
        Matrix4x4 sunViewProjection = sunView * sunProjection;

        // Point light 2 follows the mouse on the ground plane (z = 1).
        Ray3D mouseRay = _camera.Data.ScreenPointToRay(Input.MousePosition, MainView.Size);
        if (Math.Abs(mouseRay.Displacement.Z) > 0.001f)
        {
            float t = (1.0f - mouseRay.Origin.Z) / mouseRay.Displacement.Z;
            if (t > 0)
            {
                Vector3 mouseWorld = mouseRay.Origin + mouseRay.Displacement * t;
                _pointLights[2] = new PBRDeferredPipeline.PointLight(mouseWorld, new Vector3(0.4f, 1.0f, 0.6f), 6.0f);
            }
        }

        Matrix4x4.Invert(_camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);

        _lightingData.InvViewProjection = invViewProjection;
        _lightingData.SunViewProjection = sunViewProjection;
        _lightingData.CameraPosition = new Vector4(_camera.Transform.Position, 1.0f);
        _lightingData.SunDirection = new Vector4(sunDirection, 0);
        _lightingData.SunColorAndIntensity = new Vector4(_sunColor, _sunIntensity);
        _lightingData.SkyTopColor = new Vector4(_skyTopColor, 1.0f);
        _lightingData.SkyBottomColor = new Vector4(_skyBottomColor, 1.0f);
        _lightingData.SetPointLights(_pointLights);
        _lightingData.Params = new Vector4(
            _shadowEnabled ? 1.0f : 0.0f,
            1.0f,
            _pipeline.ShadowMapSize,
            _sunDiscEnabled ? 1.0f : 0.0f);
    }

    private void RenderFrame()
    {
        // 1. Shadow map pass (only objects that cast shadows).
        _pipeline.BeginShadowPass(_lightingData.SunViewProjection);
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (sceneObject.CastsShadow)
            {
                _pipeline.DrawShadow(sceneObject.Mesh, sceneObject.WorldMatrix);
            }
        }
        _pipeline.EndShadowPass();

        // 2. G-buffer pass.
        _pipeline.BeginGBufferPass();
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            _pipeline.DrawGBuffer(sceneObject.Mesh, sceneObject.WorldMatrix,
                new Vector4(sceneObject.BaseColor, 1.0f),
                new Vector4(sceneObject.Metallic, sceneObject.Roughness, sceneObject.AmbientOcclusion, 1.0f));
        }
        _pipeline.EndGBufferPass();

        // 3. Deferred lighting into the engine's HDR main target.
        _pipeline.RenderLighting(MainFrameBuffer, ref _lightingData);
    }

    private void DrawImGuiPanel()
    {
        ImGui.Begin("PBR Deferred");

        if (ImGui.CollapsingHeader("Sun Light"))
        {
            ImGui.SliderFloat3("Direction", ref _sunDirection, -1.0f, 1.0f);
            ImGui.ColorEdit3("Color", ref _sunColor);
            ImGui.SliderFloat("Intensity", ref _sunIntensity, 0.0f, 30.0f);
            ImGui.Checkbox("Shadows", ref _shadowEnabled);
            ImGui.Checkbox("Sun disc", ref _sunDiscEnabled);
        }

        if (ImGui.CollapsingHeader("Point Lights"))
        {
            ImGui.SliderFloat("Light 0 Intensity", ref _pointLights[0].ColorAndIntensity.W, 0.0f, 30.0f);
            ImGui.SliderFloat("Light 1 Intensity", ref _pointLights[1].ColorAndIntensity.W, 0.0f, 30.0f);
            ImGui.SliderFloat("Light 2 Intensity", ref _pointLights[2].ColorAndIntensity.W, 0.0f, 30.0f);
            ImGui.SliderFloat("Light 3 Intensity", ref _pointLights[3].ColorAndIntensity.W, 0.0f, 30.0f);
        }

        if (ImGui.CollapsingHeader("Sky"))
        {
            ImGui.ColorEdit3("Top", ref _skyTopColor);
            ImGui.ColorEdit3("Bottom", ref _skyBottomColor);
        }

        if (ImGui.CollapsingHeader("Material"))
        {
            string[] objectNames = _objects.Select(o => o.Mesh.Name).ToArray();
            ImGui.Combo("Object", ref _selectedObject, objectNames, objectNames.Length);
            _selectedObject = Math.Clamp(_selectedObject, 0, _objects.Count - 1);

            SceneObject sceneObject = _objects[_selectedObject];
            Vector3 baseColor = sceneObject.BaseColor;
            if (ImGui.ColorEdit3("Base Color", ref baseColor))
            {
                sceneObject.BaseColor = baseColor;
            }
            ImGui.SliderFloat("Metallic", ref sceneObject.Metallic, 0.0f, 1.0f);
            ImGui.SliderFloat("Roughness", ref sceneObject.Roughness, 0.0f, 1.0f);
            ImGui.SliderFloat("AO", ref sceneObject.AmbientOcclusion, 0.0f, 1.0f);
            ImGui.Checkbox("Cast Shadow", ref sceneObject.CastsShadow);
        }

        ImGui.Separator();
        ImGui.Checkbox("Animate Objects", ref _animateObjects);

        ImGui.End();
    }

    private void BuildScene()
    {
        // Ground.
        _objects.Add(new SceneObject
        {
            Mesh = _groundMesh,
            BaseColor = Vector3.One,
            Metallic = 0.0f,
            Roughness = 0.85f,
            AmbientOcclusion = 1.0f,
        });
        // Material variety: gold / mirror / rough red / plastic / copper / dark metal / ceramic / green.
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh,
            Transform = new Transform3D(new Vector3(-3.5f, -2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(1.0f, 0.766f, 0.336f),
            Metallic = 1.0f,
            Roughness = 0.25f,
            SpinSpeed = 0.5f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh,
            Transform = new Transform3D(new Vector3(3.5f, -2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(0.92f, 0.93f, 0.95f),
            Metallic = 1.0f,
            Roughness = 0.05f,
            SpinSpeed = -0.4f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh,
            Transform = new Transform3D(new Vector3(3.5f, 2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(0.75f, 0.1f, 0.12f),
            Metallic = 0.05f,
            Roughness = 0.85f,
            FloatSpeed = 1.2f,
            FloatPhase = 1.6f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh,
            Transform = new Transform3D(new Vector3(-3.5f, 2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(0.9f, 0.9f, 0.9f),
            Metallic = 0.0f,
            Roughness = 0.55f,
            FloatSpeed = 0.9f,
            FloatPhase = 1.6f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh,
            Transform = new Transform3D(new Vector3(-5.5f, 0, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.1f, 0.35f, 0.8f),
            Metallic = 0.0f,
            Roughness = 0.35f,
            SpinSpeed = 0.6f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh,
            Transform = new Transform3D(new Vector3(5.5f, 0, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.85f, 0.45f, 0.2f),
            Metallic = 0.95f,
            Roughness = 0.3f,
            SpinSpeed = -0.5f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh,
            Transform = new Transform3D(new Vector3(-1.8f, -5.5f, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.2f, 0.6f, 0.2f),
            Metallic = 0.0f,
            Roughness = 0.9f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh,
            Transform = new Transform3D(new Vector3(-1.8f, 5.5f, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.4f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.4f, 0.4f, 0.45f),
            Metallic = 0.9f,
            Roughness = 0.7f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh,
            Transform = new Transform3D(new Vector3(0, 0, 2.6f), Quaternion.Identity, new Vector3(2.6f)),
            BaseColor = new Vector3(0.25f, 0.3f, 0.35f),
            Metallic = 0.85f,
            Roughness = 0.45f,
            CastsShadow = false,
        });
    }

    private Texture2D CreateCheckerTexture(int size)
    {
        const int tileSize = 16;
        byte[] data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                byte value = light ? (byte)210 : (byte)105;
                int index = (y * size + x) * 4;
                data[index] = value;
                data[index + 1] = value;
                data[index + 2] = value;
                data[index + 3] = 255;
            }
        }
        return RenderingSystem.CreateTexture2D(data, (uint)size, (uint)size,
            new ImageLoadOption(format: PixelFormat.RGBA8UnormSrgb, addressMode: AddressMode.Repeat, filterMode: FilterMode.Linear, name: "checker_albedo"));
    }

    private PrimitiveMesh CreateCubeMesh()
    {
        // 24 vertices, one quad per face, outward normals, CCW winding.
        Span<VertexPositionNormalTexture> vertices = stackalloc VertexPositionNormalTexture[24];
        Span<ushort> indices = stackalloc ushort[36];

        // face data: normal, tangent axis a, tangent axis b (a x b == normal)
        Span<Vector3> normals = stackalloc Vector3[]
        {
            new(0, 0, 1), new(0, 0, -1),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
        };
        Span<Vector3> aAxes = stackalloc Vector3[]
        {
            new(1, 0, 0), new(1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
        Span<Vector3> bAxes = stackalloc Vector3[]
        {
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, 1),
            new(1, 0, 0), new(1, 0, 0),
        };

        int vertexIndex = 0;
        int indexIndex = 0;
        for (int face = 0; face < 6; face++)
        {
            Vector3 normal = normals[face];
            Vector3 a = aAxes[face];
            Vector3 b = bAxes[face];
            Vector3 center = normal * 0.5f;
            Vector3 aHalf = a * 0.5f;
            Vector3 bHalf = b * 0.5f;

            Span<Vector2> uvs = stackalloc Vector2[]
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1),
            };
            Span<Vector3> corners = stackalloc Vector3[]
            {
                center - aHalf - bHalf, center + aHalf - bHalf, center + aHalf + bHalf, center - aHalf + bHalf,
            };

            for (int i = 0; i < 4; i++)
            {
                vertices[vertexIndex] = new VertexPositionNormalTexture(corners[i], normal, uvs[i]);
                vertexIndex++;
            }

            indices[indexIndex++] = (ushort)(face * 4 + 0);
            indices[indexIndex++] = (ushort)(face * 4 + 1);
            indices[indexIndex++] = (ushort)(face * 4 + 2);
            indices[indexIndex++] = (ushort)(face * 4 + 0);
            indices[indexIndex++] = (ushort)(face * 4 + 2);
            indices[indexIndex++] = (ushort)(face * 4 + 3);
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "cube");
    }

    private PrimitiveMesh CreateSphereMesh(int segmentsU, int segmentsV)
    {
        int vertexCount = (segmentsU + 1) * (segmentsV + 1);
        int indexCount = segmentsU * segmentsV * 6;
        VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[vertexCount];
        ushort[] indices = new ushort[indexCount];

        int vertexIndex = 0;
        for (int v = 0; v <= segmentsV; v++)
        {
            float phi = v * MathF.PI / segmentsV; // 0 at +Z pole to PI at -Z pole
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);
            for (int u = 0; u <= segmentsU; u++)
            {
                float theta = u * (MathF.PI * 2.0f) / segmentsU;
                Vector3 position = new(
                    sinPhi * MathF.Cos(theta),
                    sinPhi * MathF.Sin(theta),
                    cosPhi);
                vertices[vertexIndex++] = new VertexPositionNormalTexture(position, position, new Vector2((float)u / segmentsU, (float)v / segmentsV));
            }
        }

        int indexIndex = 0;
        for (int v = 0; v < segmentsV; v++)
        {
            for (int u = 0; u < segmentsU; u++)
            {
                int i0 = v * (segmentsU + 1) + u;
                int i1 = i0 + 1;
                int i2 = i0 + (segmentsU + 1);
                int i3 = i2 + 1;
                // CCW when viewed from outside.
                indices[indexIndex++] = (ushort)i0;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i3;
            }
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "sphere");
    }

    private PrimitiveMesh CreateGroundMesh(float size, int segments)
    {
        int vertexCount = (segments + 1) * (segments + 1);
        int indexCount = segments * segments * 6;
        VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[vertexCount];
        ushort[] indices = new ushort[indexCount];

        const float uvTiles = 4.0f;
        int vertexIndex = 0;
        for (int j = 0; j <= segments; j++)
        {
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments * size - size * 0.5f;
                float y = (float)j / segments * size - size * 0.5f;
                vertices[vertexIndex++] = new VertexPositionNormalTexture(
                    new Vector3(x, y, 0),
                    Vector3.UnitZ,
                    new Vector2((float)i / segments * uvTiles, (float)j / segments * uvTiles));
            }
        }

        int indexIndex = 0;
        for (int j = 0; j < segments; j++)
        {
            for (int i = 0; i < segments; i++)
            {
                int i0 = j * (segments + 1) + i;
                int i1 = i0 + 1;
                int i2 = i0 + (segments + 1);
                int i3 = i2 + 1;
                // CCW when viewed from +Z.
                indices[indexIndex++] = (ushort)i0;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i3;
                indices[indexIndex++] = (ushort)i2;
            }
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "ground");
    }

    // Build a quaternion so that Transform(UnitX, q) == forward and the up axis
    // stays as close to worldUp as the forward direction allows. This is the
    // engine's equivalent of a LookAt rotation for a +X-forward / +Z-up camera.
    private static Quaternion LookRotation(Vector3 forward, Vector3 worldUp)
    {
        forward = Vector3.Normalize(forward);
        Vector3 up = Vector3.Normalize(worldUp - forward * Vector3.Dot(forward, worldUp));
        Vector3 right = Vector3.Cross(up, forward);
        Matrix4x4 m = new(
            forward.X, forward.Y, forward.Z, 0,
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }
}
