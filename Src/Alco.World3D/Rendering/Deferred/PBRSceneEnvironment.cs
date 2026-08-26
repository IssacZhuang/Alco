using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The shared scene state of a deferred PBR frame — sun/sky/shadow/GI/volumetric
/// parameters, the camera, the point-light list and the shadow cascade fitting —
/// together with the GPU buffers those parameters are assembled into
/// (a reflection-driven lighting uniform, a <see cref="ShadowCascadeData"/>
/// uniform and a point-light structured buffer).
/// <br/>This object is not a graph node and knows nothing about the render graph:
/// the nodes of a deferred pipeline read it (the lighting node's
/// <see cref="RGNode_DeferredLighting.PrepareData"/> assembles and uploads the
/// lighting data; the shadow node consumes <see cref="CascadeViewProjections"/>),
/// and the composing preset (<see cref="RenderPipelines.CreatePBRDeferred"/>) wires
/// the <see cref="ShadowEnabledChanged"/> / <see cref="VolumetricLightEnabledChanged"/>
/// notifications to the affected nodes. A user composing a custom deferred pipeline
/// by hand creates this object and does the same wiring through the same public API.
/// <br/>The per-cascade shadow view-projections live in a uniform buffer with reference
/// semantics, so render bundles recorded against the shadow pass layout stay valid
/// while the camera-fitted cascades move.
/// <br/>Cascade splits are computed by <see cref="ComputeShadowCascades"/> (PSSM,
/// camera-fitted, texel-snapped).
/// </summary>
public sealed unsafe class PBRSceneEnvironment : AutoDisposable
{
    /// <summary>
    /// A point light: position with range in world space and linear color (rgb)
    /// plus intensity (w). Uploaded as elements of a StructuredBuffer to the GPU.
    /// </summary>
    public struct PointLight
    {
        /// <summary>World-space position (xyz) and effective range / cutoff radius (w).</summary>
        public Vector4 Position;
        /// <summary>Linear color (rgb) and intensity (w). Zero intensity disables the light.</summary>
        public Vector4 ColorAndIntensity;

        /// <summary>
        /// Create a point light with a custom range.
        /// </summary>
        /// <param name="position">World-space position.</param>
        /// <param name="color">Linear color.</param>
        /// <param name="intensity">Light intensity; zero disables the light.</param>
        /// <param name="range">Cutoff radius beyond which the light contributes nothing.</param>
        public PointLight(in Vector3 position, in Vector3 color, float intensity, float range)
        {
            Position = new Vector4(position, range);
            ColorAndIntensity = new Vector4(color, intensity);
        }
    }

    /// <summary>The number of shadow cascades (atlas quadrants) the environment supports.</summary>
    public const int ShadowCascadeCount = RGNode_ShadowPass.CascadeCount;

    /// <summary>The maximum number of point lights the StructuredBuffer can hold.</summary>
    public const int MaxPointLights = 256;

    private readonly RenderingSystem _rendering;
    // Reflection-driven uniform buffer over the shared PBR _data block — no CPU
    // twin of PbrData; AssembleLightingData writes members by name at their
    // reflected offsets. Created lazily: the constructor may run before the host
    // installs the shader module resolver the reflection lookup needs.
    private UniformGraphicsBuffer? _lightingDataBuffer;
    private readonly GraphicsValueBuffer<ShadowCascadeData> _shadowDataBuffer;
    private readonly GraphicsArrayBuffer<PointLight> _pointLightBuffer;

    // Cascade state computed by ComputeShadowCascades and consumed by both the
    // shadow pass (shared array) and the lighting data assembly.
    private readonly Matrix4x4[] _cascadeViewProjections = new Matrix4x4[RGNode_ShadowPass.CascadeCount];
    private readonly float[] _cascadeSplits = new float[RGNode_ShadowPass.CascadeCount];
    private readonly float[] _cascadeTexelSizes = new float[RGNode_ShadowPass.CascadeCount];
    private readonly float[] _cascadeDepthRanges = new float[RGNode_ShadowPass.CascadeCount];

    private int _pointLightCount;
    private bool _shadowEnabled = true;
    private bool _volumetricLightEnabled;

    /// <summary>
    /// Creates the environment and its GPU buffers.
    /// </summary>
    /// <param name="rendering">The rendering system used to create the buffers.</param>
    /// <param name="shadowMapSize">The per-cascade shadow map resolution in texels
    /// (the shadow map is a 2x2 atlas of <see cref="ShadowCascadeCount"/> cascades, so
    /// the actual texture is twice this size along each axis).</param>
    public PBRSceneEnvironment(RenderingSystem rendering, uint shadowMapSize = 2048)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ShadowMapSize = shadowMapSize;
        _rendering = rendering;
        _shadowDataBuffer = rendering.CreateGraphicsValueBuffer<ShadowCascadeData>("pbr_shadow_data");
        // Point lights are uploaded as a StructuredBuffer (not cbuffer) so the
        // count is bounded only by GPU memory, not by cbuffer size limits.
        _pointLightBuffer = rendering.CreateGraphicsArrayBuffer<PointLight>(MaxPointLights, "pbr_point_lights");
    }

    /// <summary>The width of one shadow cascade (atlas quadrant) in texels.</summary>
    public uint ShadowMapSize { get; }

    // ── Camera ──

    /// <summary>
    /// The camera the environment reads for lighting data (inverse view-projection,
    /// position) and shadow cascade fitting. The caller must keep the camera object
    /// updated (e.g. <c>UpdateMatrixToGPU</c>) before rendering each frame.
    /// </summary>
    public CameraPerspectiveBuffer? Camera { get; set; }

    /// <summary>
    /// The shared per-frame camera-space cascade view-projection matrices (length
    /// <see cref="ShadowCascadeCount"/>), filled by <see cref="ComputeShadowCascades"/>
    /// and consumed by <see cref="RGNode_ShadowPass"/> (constructed with this array).
    /// </summary>
    public Matrix4x4[] CascadeViewProjections => _cascadeViewProjections;

    /// <summary>
    /// The depth range of each cascade's orthographic projection in world units
    /// (texelZ * shadow map size), filled by <see cref="ComputeShadowCascades"/>.
    /// Used to convert world-space error tolerances into shadow NDC depth units
    /// (e.g. the voxel GI's RSM depth-match window).
    /// </summary>
    public float[] CascadeDepthRanges => _cascadeDepthRanges;

    /// <summary>
    /// The radial end distance of each cascade in world units, filled by
    /// <see cref="ComputeShadowCascades"/> (read by GI renderers that fit the
    /// same cascades).
    /// </summary>
    public float[] CascadeSplits => _cascadeSplits;

    /// <summary>
    /// The world units per shadow texel of each cascade, filled by
    /// <see cref="ComputeShadowCascades"/> (read by GI renderers).
    /// </summary>
    public float[] CascadeTexelSizes => _cascadeTexelSizes;

    /// <summary>The number of active point lights set by the last point-light upload.</summary>
    public int PointLightCount => _pointLightCount;

    // ── GPU buffers (bound to lighting / volumetric / forward / GI materials) ──

    /// <summary>
    /// The cascade VP data buffer (per-cascade light view-projection matrices).
    /// Passed to <see cref="ShadowRenderer"/> so its materials can bind it.
    /// </summary>
    public GraphicsBuffer ShadowDataBuffer => _shadowDataBuffer;

    /// <summary>
    /// The deferred lighting data buffer (per-frame sun, sky, cascade and camera
    /// constants). Shared with forward renderers so they can evaluate the same PBR.
    /// </summary>
    public GraphicsBuffer LightingDataBuffer => _lightingDataBuffer ??= CreateLightingDataBuffer();

    private UniformGraphicsBuffer CreateLightingDataBuffer()
        => _rendering.CreateUniformGraphicsBuffer(
            _rendering.ShaderSystem.GetLibrary("alco_world3d_pbr_common").Reflection.UniformBlocks.First(block => block.Name == "_data"),
            "pbr_lighting_data");

    /// <summary>
    /// The GPU buffer holding the point light array. Read by the lighting pass
    /// and GI renderers directly.
    /// </summary>
    public GraphicsBuffer PointLightBuffer => _pointLightBuffer;

    /// <summary>The typed cascade data buffer, for the shadow pass node.</summary>
    internal GraphicsValueBuffer<ShadowCascadeData> ShadowDataBufferTyped => _shadowDataBuffer;

    // ── Scene properties (caller-set each frame) ──

    /// <summary>Normalized direction the sun light travels.</summary>
    public Vector3 SunDirection { get; set; }

    /// <summary>Linear sun color (rgb).</summary>
    public Vector3 SunColor { get; set; } = Vector3.One;

    /// <summary>Sun light intensity multiplier.</summary>
    public float SunIntensity { get; set; } = 1.0f;

    /// <summary>Whether cascaded shadow mapping is enabled. Disabling culls the
    /// shadow pass entirely (the lighting pass stops reading the shadow map) — the
    /// node synchronization is performed by the composition wiring
    /// <see cref="ShadowEnabledChanged"/>.</summary>
    public bool ShadowEnabled
    {
        get => _shadowEnabled;
        set
        {
            if (_shadowEnabled == value)
            {
                return;
            }
            _shadowEnabled = value;
            ShadowEnabledChanged?.Invoke(value);
        }
    }

    /// <summary>Raised when <see cref="ShadowEnabled"/> changes. The composing preset
    /// wires this to the shadow pass node's <see cref="IRenderNode.IsEnabled"/> and the
    /// lighting node's <see cref="RGNode_DeferredLighting.ShadowMapEnabled"/>.</summary>
    public event Action<bool>? ShadowEnabledChanged;

    /// <summary>Distance beyond which shadows are not rendered. The default is
    /// tuned on the Bistro scenes and fixed regardless of scene scale (open-world
    /// design).</summary>
    public float ShadowDistance { get; set; } = 192f;

    /// <summary>How far the light-space depth range extends toward the sun for off-screen
    /// casters, in world units. Defaults to matching <see cref="ShadowDistance"/>: receivers
    /// beyond the shadow distance are unshadowed anyway, so that bound is safe at any
    /// scene scale.</summary>
    public float ShadowCasterExtension { get; set; } = 192f;

    /// <summary>PSSM split blend: 1 = fully logarithmic, 0 = fully uniform.</summary>
    public float ShadowSplitLambda { get; set; } = 0.6f;

    /// <summary>Sun shadow penumbra tightness (0 = linear PCF average, 1 = full
    /// power-curve remap; cascade 0 uses a stronger exponent than the outer
    /// cascades to keep contact shadows hard while edges stay soft).</summary>
    public float ShadowTightness { get; set; } = 1.0f;

    /// <summary>Whether the physical-sky sun disc is visible.</summary>
    public bool SunDiscEnabled { get; set; } = true;

    /// <summary>Sun disc cosine angular threshold (higher = smaller disc).</summary>
    public float SunDiscSize { get; set; } = 0.9995f;

    /// <summary>Sun disc HDR visual brightness (independent of lighting intensity).</summary>
    public float SunDiscBrightness { get; set; } = 18.0f;

    /// <summary>Atmosphere params: x=rayleighScale, y=mieScale, z=miePhaseG, w=exposure.</summary>
    public Vector4 SkyParams { get; set; } = new(1.0f, 0.3f, 0.9f, 1.0f);

    /// <summary>Atmosphere params: x=starIntensity, y=nightFloor, z=sunRadianceScale, w=ambientFloor.</summary>
    public Vector4 SkyParams2 { get; set; } = new(1.0f, 0.05f, 20.0f, 0.25f);

    /// <summary>Filtered physical-sky radiance at the horizon.</summary>
    public Vector3 SkyHorizonColor { get; set; }

    /// <summary>Filtered physical-sky radiance at the zenith.</summary>
    public Vector3 SkyZenithColor { get; set; }

    /// <summary>Saturation of the sky colors used for ambient lighting and GI
    /// (1 = full physical color, 0 = luminance-only neutral). The atmosphere is
    /// rendered with single scattering only, which is bluer than the real sky
    /// whose multiple scattering whitens ambient light; blending toward
    /// luminance keeps sky GI intensity brightening the scene instead of
    /// tinting it blue. The visible sky is not affected.</summary>
    public float SkyGiSaturation { get; set; } = 0.6f;

    /// <summary>
    /// Debug view of the deferred lighting pass, selected through the lighting
    /// shader's <c>MainPS&lt;let DebugView&gt;</c> specialization axis (one
    /// compiled variant per view; the non-debug variant carries no branch cost).
    /// The composing preset wires <see cref="LightingDebugViewChanged"/> to the
    /// lighting node's material swap.
    /// </summary>
    public LightingDebugView LightingDebugView
    {
        get => _lightingDebugView;
        set
        {
            if (_lightingDebugView == value)
            {
                return;
            }
            _lightingDebugView = value;
            LightingDebugViewChanged?.Invoke(value);
        }
    }

    private LightingDebugView _lightingDebugView;

    /// <summary>Raised when <see cref="LightingDebugView"/> changes. The composing
    /// preset wires this to the lighting node's specialization swap.</summary>
    public event Action<LightingDebugView>? LightingDebugViewChanged;

    /// <summary>Whether GI contributes to the lighting pass.</summary>
    public bool GiEnabled { get; set; } = true;

    /// <summary>Diffuse GI strength multiplier.</summary>
    public float GiDiffuseStrength { get; set; } = 1.0f;

    /// <summary>Specular GI strength multiplier.</summary>
    public float GiSpecularStrength { get; set; } = 1f;

    /// <summary>Whether volumetric light (god rays) contributes to the frame. The node
    /// synchronization is performed by the composition wiring
    /// <see cref="VolumetricLightEnabledChanged"/>.</summary>
    public bool VolumetricLightEnabled
    {
        get => _volumetricLightEnabled;
        set
        {
            if (_volumetricLightEnabled == value)
            {
                return;
            }
            _volumetricLightEnabled = value;
            VolumetricLightEnabledChanged?.Invoke(value);
        }
    }

    /// <summary>Raised when <see cref="VolumetricLightEnabled"/> changes. The composing
    /// preset wires this to the volumetric light overlay node's
    /// <see cref="IRenderNode.IsEnabled"/>.</summary>
    public event Action<bool>? VolumetricLightEnabledChanged;

    /// <summary>Volumetric light intensity multiplier (overall brightness of light shafts).</summary>
    public float VolumetricLightIntensity { get; set; } = 0.5f;

    /// <summary>Volumetric fog density (extinction coefficient; higher = thicker fog).</summary>
    public float VolumetricLightDensity { get; set; } = 0.002f;

    /// <summary>
    /// Scale height for the height-falloff density model. Fog density decays
    /// exponentially above ground level with this height constant. Only used
    /// when the shader is compiled with VL_DENSITY_HEIGHT_FALLOFF.
    /// </summary>
    public float VolumetricLightHeightScale { get; set; } = 5.0f;

    /// <summary>Henyey-Greenstein phase anisotropy g (0=isotropic, >0=forward scattering).</summary>
    public float VolumetricLightPhaseG { get; set; } = 0.9f;

    // ── Volumetric cloud shadows (written by the clouds plugin; consumed by
    // AssembleLightingData one frame later, in lockstep with the coverage
    // texture the plugin bakes) ──

    /// <summary>Cloud shadow strength multiplying the direct sun dimming (0 = off).</summary>
    public float CloudShadowStrength { get; set; }

    /// <summary>World altitude of the cloud shadow projection plane (meters, mid-slab).</summary>
    public float CloudShadowPlaneAltitude { get; set; } = 3250.0f;

    /// <summary>Half extent of the cloud shadow coverage window around the camera (meters).</summary>
    public float CloudShadowExtent { get; set; } = 20000.0f;

    /// <summary>
    /// Upload point lights to the GPU StructuredBuffer. Call once per frame before
    /// rendering; the active count is tracked internally.
    /// An upload identical to the current contents is skipped (no GPU upload).
    /// </summary>
    /// <param name="lights">Active point lights; excess lights beyond
    /// <see cref="MaxPointLights"/> are silently dropped.</param>
    public void UpdatePointLights(ReadOnlySpan<PointLight> lights)
    {
        int count = Math.Min(lights.Length, MaxPointLights);
        // Compare against the currently uploaded data: identical light arrays
        // skip the GPU upload entirely.
        bool unchanged = count == _pointLightCount
            && MemoryMarshal.AsBytes(lights.Slice(0, count))
                .SequenceEqual(MemoryMarshal.AsBytes(_pointLightBuffer.AsSpan().Slice(0, count)));
        if (unchanged)
        {
            return;
        }

        var span = _pointLightBuffer.AsSpan();
        for (int i = 0; i < count; i++)
        {
            span[i] = lights[i];
        }
        _pointLightBuffer.UpdateBufferRanged(0, (uint)count);
        _pointLightCount = count;
    }

    /// <summary>
    /// Compute cascaded shadow map data for a directional sun: per-cascade light
    /// view-projection matrices, split boundaries and world texel sizes, stored
    /// internally for use by the shadow and lighting passes.
    /// <br/>Splits follow the practical split scheme (log/uniform blend controlled by
    /// <see cref="ShadowSplitLambda"/>) on radial camera distance. The light space is a
    /// pure rotation (camera-independent) and each cascade fits a fixed-radius bounding
    /// sphere of its frustum slice, snapped to texel increments, so the shadow map stays
    /// stable when the camera moves or rotates.
    /// </summary>
    /// <param name="cameraNear">Near boundary of cascade 0, typically the camera near plane distance.</param>
    /// <exception cref="InvalidOperationException">No camera is set (<see cref="Camera"/>).</exception>
    public void ComputeShadowCascades(float cameraNear)
    {
        if (Camera == null)
        {
            throw new InvalidOperationException("ComputeShadowCascades requires a camera (set Camera first).");
        }

        Matrix4x4.Invert(Camera.Data.ViewProjectionMatrix, out Matrix4x4 invCameraViewProjection);
        Vector3 cameraPosition = Camera.Transform.Position;
        Vector3 sunDirection = SunDirection;
        uint shadowMapSize = ShadowMapSize;
        float shadowDistance = ShadowDistance;
        float casterExtension = ShadowCasterExtension;
        float splitLambda = ShadowSplitLambda;

        // Frustum edge rays: the four far-plane corners in world space.
        Span<Vector3> edgeRays = stackalloc Vector3[4];
        int rayIndex = 0;
        for (int y = -1; y <= 1; y += 2)
        {
            for (int x = -1; x <= 1; x += 2)
            {
                Vector4 corner = Vector4.Transform(new Vector4(x, y, 1.0f, 1.0f), invCameraViewProjection);
                Vector3 farCorner = new Vector3(corner.X, corner.Y, corner.Z) / corner.W;
                edgeRays[rayIndex++] = Vector3.Normalize(farCorner - cameraPosition);
            }
        }

        // Camera-independent light space: a pure rotation around the world origin, so
        // world geometry stays still in light space while the camera moves.
        Vector3 up = Math.Abs(Vector3.Dot(sunDirection, Vector3.UnitZ)) > 0.95f ? Vector3.UnitY : Vector3.UnitZ;
        Matrix4x4 lightView = Matrix4x4.CreateLookAtLeftHanded(Vector3.Zero, sunDirection, up);

        float sliceNear = cameraNear;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int c = 0; c < RGNode_ShadowPass.CascadeCount; c++)
        {
            float p = (c + 1) / (float)RGNode_ShadowPass.CascadeCount;
            float logarithmic = cameraNear * MathF.Pow(shadowDistance / cameraNear, p);
            float uniform = cameraNear + (shadowDistance - cameraNear) * p;
            float sliceFar = splitLambda * logarithmic + (1.0f - splitLambda) * uniform;
            _cascadeSplits[c] = sliceFar;

            // Frustum slice corners on the edge rays.
            Vector3 center = Vector3.Zero;
            for (int r = 0; r < 4; r++)
            {
                corners[r] = cameraPosition + edgeRays[r] * sliceNear;
                corners[r + 4] = cameraPosition + edgeRays[r] * sliceFar;
                center += corners[r] + corners[r + 4];
            }
            center /= 8.0f;

            // Fit a bounding sphere: its radius is invariant to camera rotation and
            // translation, so the texel grid has a constant world size.
            float radius = 0.0f;
            for (int r = 0; r < 8; r++)
            {
                radius = Math.Max(radius, Vector3.Distance(corners[r], center));
            }

            // Grow by one texel per side so the sphere stays inside the snapped box
            // (snapping shifts the box by up to ~0.71 texels diagonally).
            float texel = radius * 2.0f / shadowMapSize;
            radius += texel;
            texel = radius * 2.0f / shadowMapSize;

            // Snap the box center to whole texels so it steps discretely instead of
            // sliding continuously under camera movement.
            Vector3 centerLight = Vector3.Transform(center, lightView);
            centerLight.X = MathF.Floor(centerLight.X / texel) * texel;
            centerLight.Y = MathF.Floor(centerLight.Y / texel) * texel;

            // Depth range: the bounding sphere's Z extent. Do NOT tighten this to the
            // 8 slice corners' min/max Z — the radial-split slice is a spherical shell
            // whose Z extent exceeds the corner hull whenever the light travel direction
            // falls inside the view cone: receivers near the split then project past the
            // ortho far plane, hit the ndc.z > 1 early-out in the lighting shader and
            // are treated as fully lit (a lit band of missing shadow before each split).
            // The near plane extends toward the sun for off-screen casters (negative
            // values are legal for ortho).
            float zMin = centerLight.Z - radius - casterExtension;
            float zMax = centerLight.Z + radius;
            float texelZ = (zMax - zMin) / shadowMapSize;
            zMin = MathF.Floor(zMin / texelZ) * texelZ;
            zMax = zMin + texelZ * shadowMapSize;

            Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenterLeftHanded(
                centerLight.X - radius, centerLight.X + radius,
                centerLight.Y - radius, centerLight.Y + radius,
                zMin, zMax);
            _cascadeViewProjections[c] = lightView * ortho;
            _cascadeTexelSizes[c] = texel;
            _cascadeDepthRanges[c] = texelZ * shadowMapSize;

            sliceNear = sliceFar;
        }
    }

    // Rec.709 luminance weights for desaturating ambient sky colors.
    private static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

    // Every consumer of the two sky colors (diffuse baseline, voxel GI,
    // volumetric clouds) treats them as ambient irradiance, so the
    // SkyGiSaturation blend is applied once here at assembly.
    private Vector3 DesaturateAmbientSky(Vector3 color)
    {
        float luminance = Vector3.Dot(color, LuminanceWeights);
        return Vector3.Lerp(new Vector3(luminance), color, SkyGiSaturation);
    }

    /// <summary>
    /// Assemble <see cref="CurrentLightingData"/> from the scene properties, camera and
    /// cascade state. Called by the lighting node (followed by
    /// <see cref="UploadLightingData"/> for the final GPU upload) and by GI renderers
    /// that evaluate the same lighting (so their trace pass sees current data).
    /// </summary>
    /// <param name="invViewProjection">The inverse of the camera view-projection matrix.</param>
    /// <param name="gbuffer">The G-buffer facade of the frame (for the viewport size).</param>
    /// <param name="giDiffuseActive">Whether a diffuse GI input is wired into the
    /// lighting pass this frame (combined with <see cref="GiEnabled"/>).</param>
    /// <exception cref="InvalidOperationException">No camera is set (<see cref="Camera"/>).</exception>
    public void AssembleLightingData(Matrix4x4 invViewProjection, RenderTexture gbuffer, bool giDiffuseActive)
    {
        if (Camera == null)
        {
            throw new InvalidOperationException("AssembleLightingData requires a camera (set Camera first).");
        }
        UniformGraphicsBuffer data = LightingDataBuffer as UniformGraphicsBuffer
            ?? throw new InvalidOperationException("The lighting data buffer must be the reflection-driven uniform buffer.");
        data.SetValue("invViewProjection", invViewProjection);
        data.SetValues("sunViewProjection", _cascadeViewProjections);
        data.SetValue("cameraPosition", new Vector4(Camera.Transform.Position, 1.0f));
        data.SetValue("sunDirection", new Vector4(SunDirection, 0));
        data.SetValue("sunColorAndIntensity", new Vector4(SunColor, SunIntensity));
        data.SetValue("skyParams", SkyParams);
        data.SetValue("skyParams2", SkyParams2);
        data.SetValue("skyHorizonColor", new Vector4(DesaturateAmbientSky(SkyHorizonColor), 0.0f));
        data.SetValue("skyZenithColor", new Vector4(DesaturateAmbientSky(SkyZenithColor), 0.0f));
        data.SetValue("shadowEnabled", ShadowEnabled);
        data.SetValue("numPointLights", (uint)_pointLightCount);
        data.SetValue("shadowMapSize", (float)ShadowMapSize);
        data.SetValue("sunDiscEnabled", SunDiscEnabled);
        data.SetValue("cascadeSplits", new Vector4(
            _cascadeSplits[0], _cascadeSplits[1], _cascadeSplits[2], _cascadeSplits[3]));
        data.SetValue("cascadeTexelSizes", new Vector4(
            _cascadeTexelSizes[0], _cascadeTexelSizes[1], _cascadeTexelSizes[2], _cascadeTexelSizes[3]));
        data.SetValue("shadowTightness", ShadowTightness);
        data.SetValue("viewportWidth", gbuffer.Width);
        data.SetValue("viewportHeight", gbuffer.Height);
        data.SetValue("giEnabled", giDiffuseActive && GiEnabled);
        data.SetValue("giDiffuseStrength", GiDiffuseStrength);
        data.SetValue("giSpecularStrength", GiSpecularStrength);
        data.SetValue("sunDiscSize", SunDiscSize);
        data.SetValue("sunDiscBrightness", SunDiscBrightness);
        data.SetValue("volumetricLightEnabled", VolumetricLightEnabled);
        data.SetValue("volumetricLightIntensity", VolumetricLightIntensity);
        data.SetValue("fogDensity", VolumetricLightDensity);
        data.SetValue("heightScaleHeight", VolumetricLightHeightScale);
        data.SetValue("phaseG", VolumetricLightPhaseG);
        data.SetValue("cloudShadowStrength", CloudShadowStrength);
        data.SetValue("cloudShadowPlaneAltitude", CloudShadowPlaneAltitude);
        data.SetValue("cloudShadowExtent", CloudShadowExtent);
        data.SetValue("cloudShadowEnabled", CloudShadowStrength > 0.0f);
    }

    /// <summary>
    /// Flushes the members staged by <see cref="AssembleLightingData"/> to the
    /// GPU lighting data buffer. Runs before the lighting pass is recorded (the
    /// graph's deferred submission requires uploads first).
    /// </summary>
    public void UploadLightingData()
    {
        _lightingDataBuffer?.Flush();
    }
    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lightingDataBuffer?.Dispose();
            _shadowDataBuffer.Dispose();
            _pointLightBuffer.Dispose();
        }
    }
}
