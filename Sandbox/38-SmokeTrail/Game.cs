using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;
using SandboxUtils;

/// <summary>
/// First-person-shooter template demonstrating the 3D GPU trail renderer
/// (<see cref="GpuTrailSystem3D"/>, Alco.Rendering): left-click fires a bright
/// tracer bullet that leaves a camera-facing smoke ribbon behind it (the
/// TrailSurface3DSmoke surface: turbulence wander, buoyant rise, noise-eroded
/// dissipation). Bullets fly straight, hit the ground/obstacles/targets with a
/// segment sweep, and the trail finishes at the impact point and fades out.
/// Targets respawn at a random spot when hit; the HUD tracks accuracy.
/// <br/>Controls: WASD move, Shift sprint, Space jump, mouse look, left mouse
/// fires, ESC releases the cursor (click to capture it again), ESC with the
/// cursor free exits.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N] — captures after N frames;
/// screenshot mode auto-fires a volley from a fixed spot and then steps to a side
/// vantage, so the trails read broadside in the capture. --plaintrail swaps the
/// smoke surface for the plain default surface (debugging).
/// </summary>
public class Game : GameEngine
{
    // ── Rendering ────────────────────────────────────────────────────────────
    private readonly RenderPipeline _pipeline;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly GpuTrailSystem3D _trails;
    private readonly GraphicsMaterial _sceneMaterial;
    private readonly Mesh _cubeMesh;
    private readonly TrailEffect3D _bulletTrailEffect;

    // ── Player (the FPS controller state) ────────────────────────────────────
    private Vector3 _eyePosition = new(-17f, -11f, EyeHeight);
    private float _yaw = 0.57f;
    private float _pitch = 0.06f;
    private float _verticalVelocity;
    private bool _captured = true;
    private const float EyeHeight = 1.7f;
    private const float WalkSpeed = 6f;
    private const float SprintScale = 1.9f;
    private const float JumpSpeed = 5.2f;
    private const float Gravity = 13.5f;
    private const float PlayerRadius = 0.35f;
    private const float RawMouseCountScale = 5f;

    // ── Bullets ──────────────────────────────────────────────────────────────
    private sealed class Bullet
    {
        public required Vector3 Position;
        public required Vector3 Velocity;
        public required TrailEffectInstance3D Trail;
        public float Age;
    }

    private readonly List<Bullet> _bullets = [];
    private float _fireCooldown;
    private const float FireInterval = 0.13f;
    private const float BulletSpeed = 52f;
    private const float BulletMaxAge = 3.5f;
    private const float GroundHitZ = 0.04f;

    // ── World: static obstacles and reactive targets (AABBs double as the
    // bullet/player collision set) ────────────────────────────────────────────
    private readonly record struct Box(Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
    }

    private readonly List<(Box Box, ColorFloat Color)> _props = [];
    private readonly List<Box> _targets = [];
    private FastRandom _random = new(987654);
    private int _shots;
    private int _hits;

    // ── Screenshot mode (see the class remarks) ──────────────────────────────
    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;
    // Screenshot volley bookkeeping: the capture vantage leaves the muzzle so the
    // ribbons are seen from the side, not edge-on down their flight path.
    private Vector3 _volleyOrigin;
    private float _volleyMidYaw;

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 90;
        if (_screenshotPath != null)
        {
            _swapchainCapture = SwapchainCapture;
        }

        AddSystem(new ImGUISystem(this));

        // Normal (non-reversed) depth: the plain pipeline clears depth to 1.
        _camera = RenderingSystem.CreateCameraPerspective(1.25f, 16f / 9, 0.08f, 400f);

        // The 3D trail system: one shared point buffer for every bullet trail.
        _trails = new GpuTrailSystem3D(RenderingSystem, pointCapacity: 1 << 17, trailSlots: 256)
        {
            Camera = _camera,
        };

        _pipeline = new RenderPipeline(RenderingSystem, new RenderPipeline.Descriptor
        {
            SceneLayout = RenderingSystem.PreferredHDRPass,
            BlitShader = BuiltInAssets.Shader_Blit,
            Width = MainView.Size.X,
            Height = MainView.Size.Y,
            Name = "fps_smoke_trail",
        });
        _pipeline.ClearColor = new ColorFloat(0.36f, 0.46f, 0.62f, 1);
        _pipeline.Use(new SceneNode(this, _pipeline.Graph, _pipeline.Chain));
        var tonemapNode = new RGNode_Tonemap(
            RenderingSystem,
            _pipeline.Graph,
            _pipeline.Chain,
            _pipeline.PostProcessLayout,
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
        tonemapNode.Operator = TonemapType.ACES;
        ACESTonemapData acesData = tonemapNode.ACESData;
        acesData.Gamma = 2.2f;
        tonemapNode.ACESData = acesData;
        _pipeline.Use(tonemapNode);

        MainPresenter.OnResize += size =>
        {
            _camera.AspectRatio = (float)size.X / size.Y;
            _pipeline.Resize(size.X, size.Y);
        };

        _sceneMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Unlit, "props");
        _sceneMaterial.SetBuffer("camera", _camera);
        _sceneMaterial.DepthStencilState = DepthStencilState.Write;
        _cubeMesh = RenderingSystem.MeshCube;

        // The bullet trail: a smoke ribbon that starts thin at the muzzle, widens
        // as it dissipates and rises slightly once old. The material composes the
        // built-in smoke surface with the 3D trail pass template. --plaintrail
        // swaps in the default surface (a plain gradient ribbon) for debugging.
        TrailEffect3D trailEffect = new()
        {
            Life = 2.6f,
            Spacing = 0.4f,
            Width0 = 0.16f,
            Width1 = 1.0f,
            Opacity = 1f,
            Color0 = ColorFloat.White,
            Color1 = ColorFloat.White,
            FadeIn = 0.04f,
            FadeOut = 0.7f,
            ExpectedPoints = 512,
        };
        if (GetArgValue(args, "--plaintrail") != null)
        {
            trailEffect.Width0 = 0.12f;
            trailEffect.Width1 = 0.3f;
        }
        else
        {
            trailEffect.Material = new MaterialAsset
            {
                Name = "bullet-smoke",
                Surface = RenderingSystem.ShaderSystem.GetLibrary("TrailSurface3DSmoke"),
                Parameters = new Dictionary<string, ShaderValue>
                {
                    ["smokeColor"] = new Vector4(0.5f, 0.53f, 0.6f, 0.95f),
                    ["shape"] = new Vector4(0.35f, 0.35f, 0.55f, 0.45f),
                    ["width"] = new Vector4(0.45f, 0.5f, 0f, 0f),
                    ["edge"] = new Vector4(0.05f, 1.4f, 0.6f, 0f),
                    ["motion"] = new Vector4(0.5f, 0.35f, 0f, 0f),
                },
            };
        }
        _bulletTrailEffect = trailEffect;

        BuildWorld();
    }

    /// <summary>The arena: a ground slab, scattered cover crates and two walls, plus the targets.</summary>
    private void BuildWorld()
    {
        // The ground (top at z = 0; bullets hit the plane separately).
        _props.Add((new Box(new Vector3(-45, -45, -1), new Vector3(45, 45, 0)), new ColorFloat(0.3f, 0.32f, 0.3f, 1)));

        (Vector3 Center, Vector3 Size)[] covers =
        [
            (new Vector3(-6, 2, 1), new Vector3(2, 2, 2)),
            (new Vector3(4, 6, 0.8f), new Vector3(3, 1.4f, 1.6f)),
            (new Vector3(9, -4, 1.2f), new Vector3(1.6f, 1.6f, 2.4f)),
            (new Vector3(-2, -8, 0.6f), new Vector3(4, 1.8f, 1.2f)),
            (new Vector3(14, 8, 1.5f), new Vector3(2.5f, 2.5f, 3)),
            (new Vector3(-12, 8, 1), new Vector3(2, 3, 2)),
            (new Vector3(0, 16, 2), new Vector3(10, 1, 4)),
            (new Vector3(-18, -2, 2), new Vector3(1, 8, 4)),
        ];
        ColorFloat[] coverColors =
        [
            new ColorFloat(0.45f, 0.36f, 0.26f, 1),
            new ColorFloat(0.36f, 0.38f, 0.44f, 1),
            new ColorFloat(0.42f, 0.3f, 0.3f, 1),
            new ColorFloat(0.33f, 0.4f, 0.34f, 1),
            new ColorFloat(0.45f, 0.36f, 0.26f, 1),
            new ColorFloat(0.36f, 0.38f, 0.44f, 1),
            new ColorFloat(0.38f, 0.38f, 0.38f, 1),
            new ColorFloat(0.38f, 0.38f, 0.38f, 1),
        ];
        for (int i = 0; i < covers.Length; i++)
        {
            (Vector3 center, Vector3 size) = covers[i];
            Vector3 half = size * 0.5f;
            _props.Add((new Box(center - half, center + half), coverColors[i]));
        }

        // Targets: bright cubes on the arena floor; a bullet hit respawns them.
        for (int i = 0; i < 5; i++)
        {
            _targets.Add(RandomTargetBox());
        }
    }

    private Box RandomTargetBox()
    {
        const float size = 0.8f;
        Vector3 center = new(
            _random.NextFloat(-14f, 18f),
            _random.NextFloat(-12f, 14f),
            size * 0.5f);
        Vector3 half = new(size * 0.5f, size * 0.5f, size * 0.5f);
        return new Box(center - half, center + half);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            // The first ESC frees the cursor (for windowed convenience); with the
            // cursor already free it exits.
            if (_captured)
            {
                _captured = false;
                Input.IsMouseRelativeMode = false;
                Input.IsCursorVisible = true;
            }
            else
            {
                Stop();
            }
        }

        UpdateCapture();
        UpdatePlayer(delta);
        UpdateShooting(delta);
        UpdateBullets(delta);

        _trails.CameraPosition = _eyePosition;
        _trails.Update(delta);

        DrawHud();
        DebugStats.Text(FrameRate);

        _frameCount++;
        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _frameCount >= _screenshotFrames)
        {
            // The request completes a couple of frames later with the presented
            // frame's PNG (scene, post processing and the ImGui overlay on top).
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();
        }

        _pipeline.Render(MainPresenter.FrameBuffer);

        if (_screenshotRequest is { IsCompletedSuccessfully: true })
        {
            RenderCaptureResult result = _screenshotRequest.Result;
            if (result.Success && result.PngBytes != null)
            {
                File.WriteAllBytes(_screenshotPath!, result.PngBytes);
                Console.WriteLine($"Screenshot saved to {_screenshotPath}");
            }
            else
            {
                Console.WriteLine($"Screenshot failed: {result.Error}");
            }

            _screenshotSaved = true;
            Stop();
        }
        else if (_screenshotRequest is { IsFaulted: true })
        {
            Console.WriteLine($"Screenshot failed: {_screenshotRequest.Exception?.GetBaseException().Message}");
            Stop();
        }
    }

    /// <summary>Pointer capture: click to capture, the ESC handler above releases.</summary>
    private void UpdateCapture()
    {
        if (!_captured && MainView.IsFocused && Input.IsMouseDown(Mouse.Left))
        {
            _captured = true;
        }
        if (_captured)
        {
            Input.IsMouseRelativeMode = true;
        }
    }

    /// <summary>
    /// The FPS controller: mouse look (raw deltas in relative mode), WASD in the
    /// view plane, sprint, jump and gravity, ground clamp and a circle-vs-AABB
    /// push-out against the obstacles (the player never enters a crate).
    /// </summary>
    private void UpdatePlayer(float delta)
    {
        if (_captured && MainView.IsFocused)
        {
            Vector2 mouseDelta = Input.MouseDelta / RawMouseCountScale;
            _yaw += mouseDelta.X * 0.008f;
            _pitch = Math.Clamp(_pitch - mouseDelta.Y * 0.008f, -1.5f, 1.5f);
        }

        Vector3 forward = Direction(_pitch, _yaw);
        Vector3 flatForward = Vector3.Normalize(new Vector3(forward.X, forward.Y, 0f));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, flatForward));

        Vector3 move = Vector3.Zero;
        if (Input.IsKeyPressing(KeyCode.W)) move += flatForward;
        if (Input.IsKeyPressing(KeyCode.S)) move -= flatForward;
        if (Input.IsKeyPressing(KeyCode.D)) move += right;
        if (Input.IsKeyPressing(KeyCode.A)) move -= right;
        if (move.LengthSquared() > 0f)
        {
            float speed = WalkSpeed * (Input.IsKeyPressing(KeyCode.ShiftLeft) ? SprintScale : 1f);
            _eyePosition += Vector3.Normalize(move) * speed * delta;
        }

        // Jump and gravity against the ground plane (eye height above z = 0).
        bool grounded = _eyePosition.Z <= EyeHeight + 0.001f;
        if (grounded && Input.IsKeyDown(KeyCode.Space))
        {
            _verticalVelocity = JumpSpeed;
        }
        _verticalVelocity -= Gravity * delta;
        _eyePosition.Z += _verticalVelocity * delta;
        if (_eyePosition.Z <= EyeHeight)
        {
            _eyePosition.Z = EyeHeight;
            _verticalVelocity = 0f;
        }

        // Push the player's XY circle out of any obstacle the body overlaps in z.
        float feet = _eyePosition.Z - EyeHeight;
        foreach ((Box box, _) in _props)
        {
            if (feet + 0.2f >= box.Max.Z || _eyePosition.Z <= box.Min.Z)
            {
                continue;
            }
            Vector2 closest = new(
                Math.Clamp(_eyePosition.X, box.Min.X, box.Max.X),
                Math.Clamp(_eyePosition.Y, box.Min.Y, box.Max.Y));
            Vector2 toPlayer = new Vector2(_eyePosition.X, _eyePosition.Y) - closest;
            float distance = toPlayer.Length();
            if (distance < PlayerRadius && distance > 1e-5f)
            {
                Vector2 push = toPlayer / distance * (PlayerRadius - distance);
                _eyePosition += new Vector3(push, 0f);
            }
        }

        _camera.Transform = new Transform3D(_eyePosition, LookRotation(forward, Vector3.UnitZ));
        _camera.UpdateMatrixToGPU();
    }

    /// <summary>Left mouse (captured) fires at a fixed cadence; screenshot mode auto-fires a slow volley.</summary>
    private void UpdateShooting(float delta)
    {
        _fireCooldown -= delta;
        bool wantsFire = _captured && MainView.IsFocused && Input.IsMousePressing(Mouse.Left);
        if (_screenshotPath != null)
        {
            // Screenshot mode: a frame-keyed volley (timing-independent) from a
            // fixed spot, each shot swept a little — then the camera steps to a
            // side vantage so the capture reads the ribbons broadside (a
            // camera-facing ribbon viewed along its flight path is edge-on).
            if (_frameCount == 20)
            {
                _volleyOrigin = _eyePosition;
                _volleyMidYaw = _yaw + 0.84f;
            }
            if (_frameCount >= 20 && (_frameCount - 20) % 11 == 0 && _frameCount <= 90)
            {
                _fireCooldown = 0f;
                wantsFire = true;
                _yaw += 0.28f;
                _pitch = 0.06f + 0.05f * MathF.Sin(_frameCount * 0.21f);
            }
            if (_frameCount == 92)
            {
                Vector3 mid = Direction(0f, _volleyMidYaw);
                Vector3 sideDir = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, mid));
                _eyePosition = _volleyOrigin + sideDir * 17f + new Vector3(0f, 0f, 3.5f);
                Vector3 look = Vector3.Normalize(_volleyOrigin + mid * 13f - _eyePosition);
                _yaw = MathF.Atan2(look.Y, look.X);
                _pitch = MathF.Asin(Math.Clamp(look.Z, -1f, 1f));
            }
        }
        if (!wantsFire || _fireCooldown > 0f)
        {
            return;
        }
        _fireCooldown = FireInterval;

        Vector3 forward = Direction(_pitch, _yaw);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
        Vector3 up = Vector3.Cross(forward, right);
        Vector3 muzzle = _eyePosition + forward * 0.5f + right * 0.14f - up * 0.12f;

        if (!_trails.TryCreateInstance(_bulletTrailEffect, muzzle, out TrailEffectInstance3D trail))
        {
            // The trail budget is exhausted: skip the shot's trail, not the shot.
            trail = null!;
        }
        _bullets.Add(new Bullet
        {
            Position = muzzle,
            Velocity = forward * BulletSpeed,
            Trail = trail,
        });
        _shots++;
    }

    /// <summary>
    /// Integrates the bullets with a segment sweep against the ground, obstacles
    /// and targets. On a hit the trail finishes at the impact point and fades; in
    /// flight the head extends the ribbon. Faded trails recycle themselves.
    /// </summary>
    private void UpdateBullets(float delta)
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            Bullet bullet = _bullets[i];
            Vector3 from = bullet.Position;
            Vector3 to = from + bullet.Velocity * delta;
            bullet.Age += delta;

            if (TryHitWorld(from, to, out Vector3 hit, out int targetIndex))
            {
                bullet.Trail?.Finish(hit);
                if (targetIndex >= 0)
                {
                    _targets[targetIndex] = RandomTargetBox();
                    _hits++;
                }
                _bullets.RemoveAt(i);
                continue;
            }
            if (bullet.Age >= BulletMaxAge)
            {
                bullet.Trail?.Finish(to);
                _bullets.RemoveAt(i);
                continue;
            }
            bullet.Position = to;
            bullet.Trail?.ExtendTo(to);
        }
    }

    /// <summary>
    /// The nearest intersection of the segment with the ground plane, the obstacle
    /// boxes and the target boxes (slab test). Returns the hit point and the hit
    /// target's index (-1 for static geometry).
    /// </summary>
    private bool TryHitWorld(Vector3 from, Vector3 to, out Vector3 hit, out int targetIndex)
    {
        targetIndex = -1;
        float bestT = float.MaxValue;

        if (to.Z < GroundHitZ && from.Z > GroundHitZ)
        {
            bestT = (from.Z - GroundHitZ) / (from.Z - to.Z);
        }
        foreach ((Box box, _) in _props)
        {
            if (box.Size.Z > 0.01f && RayBox(from, to, box, out float t) && t < bestT)
            {
                bestT = t;
                targetIndex = -1;
            }
        }
        for (int i = 0; i < _targets.Count; i++)
        {
            if (RayBox(from, to, _targets[i], out float t) && t < bestT)
            {
                bestT = t;
                targetIndex = i;
            }
        }
        if (bestT == float.MaxValue)
        {
            hit = default;
            return false;
        }
        hit = from + (to - from) * bestT;
        return true;
    }

    /// <summary>Slab intersection of the segment from->to with an AABB; t in [0, 1].</summary>
    private static bool RayBox(Vector3 from, Vector3 to, in Box box, out float t)
    {
        t = 0f;
        float tMax = 1f;
        Vector3 d = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = axis == 0 ? from.X : axis == 1 ? from.Y : from.Z;
            float direction = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float min = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
            float max = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            if (MathF.Abs(direction) < 1e-8f)
            {
                if (origin < min || origin > max)
                {
                    return false;
                }
                continue;
            }
            float inv = 1f / direction;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            t = MathF.Max(t, t1);
            tMax = MathF.Min(tMax, t2);
            if (t > tMax)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The crosshair and the status line, drawn straight onto the foreground draw list.</summary>
    private void DrawHud()
    {
        ImDrawListPtr drawList = ImGui.GetForegroundDrawList();
        Vector2 center = new(MainView.Size.X * 0.5f, MainView.Size.Y * 0.5f);
        uint crossColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));
        const float arm = 7f;
        drawList.AddLine(center + new Vector2(-arm, 0), center + new Vector2(arm, 0), crossColor, 1.5f);
        drawList.AddLine(center + new Vector2(0, -arm), center + new Vector2(0, arm), crossColor, 1.5f);

        string status = $"FPS {FrameRate}   hits {_hits}/{_shots}   bullets {_bullets.Count}";
        drawList.AddText(new Vector2(12, 10), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.9f)), status);
        drawList.AddText(new Vector2(12, 26), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.55f)),
            "WASD move  Shift sprint  Space jump  LMB fire  ESC release/exit");
    }

    protected override void OnStop()
    {
        _trails.Dispose();
        _pipeline.Dispose();
    }

    private static Vector3 Direction(float pitch, float yaw)
    {
        return new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
    }

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

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }
        return null;
    }

    /// <summary>The scene content node: the arena and targets (depth-written), the
    /// tracer bullets, then the smoke trails (depth-tested, alpha-blended).</summary>
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
                foreach ((Box box, ColorFloat color) in _game._props)
                {
                    pass.DrawWithConstant(_game._cubeMesh, _game._sceneMaterial,
                        BoxConstant(box, color));
                }
                foreach (Box targetBox in _game._targets)
                {
                    pass.DrawWithConstant(_game._cubeMesh, _game._sceneMaterial,
                        BoxConstant(targetBox, new ColorFloat(1.4f, 0.25f, 0.15f, 1)));
                }
                foreach (Bullet bullet in _game._bullets)
                {
                    pass.DrawWithConstant(_game._cubeMesh, _game._sceneMaterial,
                        new PropConstant
                        {
                            Matrix = new Transform3D(bullet.Position, Quaternion.Identity) { Scale = new Vector3(0.14f) }.Matrix,
                            Color = new ColorFloat(3.2f, 2.0f, 0.7f, 1),
                        });
                }
                _game._trails.Render(pass);
            }
        }

        private static PropConstant BoxConstant(Box box, ColorFloat color)
        {
            return new PropConstant
            {
                Matrix = new Transform3D(box.Center, Quaternion.Identity) { Scale = box.Size }.Matrix,
                Color = color,
            };
        }
    }

    /// <summary>The per-draw constant of the unlit scene geometry.</summary>
    private struct PropConstant
    {
        public Matrix4x4 Matrix;
        public ColorFloat Color;
    }
}
