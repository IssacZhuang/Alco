using System.Reflection;
using Alco.Graphics;
using Alco.Graphics.NoGPU;
#nullable enable

using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Shader variant model: one Shader is one module handle; every variant axis is
// a generic value specialization passed to GetShaderModules/pipeline/material
// factories. Specializations compile lazily and cache inside the shader.
// Runs on the NoGPU device; module/reflection and resource-group behavior.
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>Verifies material specializations and inherited resource-group invalidation.</summary>
[TestFixture]
public class MaterialVariantTest
{
    // A graphics module with one <let Flag> axis: both specializations share the
    // same binding surface, so materials of either variant bind the same slots.
    private const string QuadModule = """
        import AlcoRendering_Core;

        cbuffer camera : register(b0, space0)
        {
            float4x4 viewProjection;
        };

        cbuffer material : register(b0, space1)
        {
            Texture2D albedo;
            SamplerState linearClamp;
        };

        struct Vertex
        {
            float3 position : POSITION;
            float2 uv       : TEXCOORD0;
        };

        struct V2F
        {
            float4 position : SV_POSITION;
            float2 uv       : TEXCOORD0;
        };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F output;
            output.position = mul(viewProjection, float4(input.position, 1.0));
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 MainPS<let Flag : int>(V2F input) : SV_TARGET
        {
            float dither = outputDither8Bit(input.position.xy) * 0.0;
            float4 color = sampleTex2D(albedo, linearClamp, input.uv) + dither + PI * 0.0;
            if (Flag != 0) { color.g += 0.0; }
            return color;
        }
        """;

    // A compute module with the same <let Flag> axis for the compute side.
    private const string ComputeModule = """
        import AlcoRendering_Core;

        cbuffer pass : register(b0, space0)
        {
            RWStructuredBuffer<float4> output;
        };

        cbuffer material : register(b0, space1)
        {
            Texture2D source;
        };

        [shader("compute")]
        void MainCS<let Flag : int>(uint3 dispatchThreadID : SV_DispatchThreadID)
        {
            // Compute cannot use implicit derivatives — Load instead of Sample.
            float4 value = source.Load(int3(dispatchThreadID.xy, 0));
            if (Flag != 0) { value.g += 0.0; }
            output[dispatchThreadID.x] = value;
        }
        """;

    private const string ResizeModule = """
        cbuffer masks : register(b0, space0)
        {
            Texture2D<float4> visionMask;
            Texture2D<float4> secondaryMask;
        };
        cbuffer stable : register(b0, space1)
        {
            Texture2D<float4> unchangedTexture;
        };
        [shader("vertex")]
        float4 MainVS(uint vertex : SV_VertexID) : SV_POSITION
        {
            return float4(float(vertex), 0, 0, 1);
        }
        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            return visionMask.Load(int3(0, 0, 0))
                + secondaryMask.Load(int3(0, 0, 0))
                + unchangedTexture.Load(int3(0, 0, 0));
        }
        """;

    private sealed class CountingNoDevice : NoDevice
    {
        private int _createdResourceGroupCount;

        /// <summary>Gets the number of resource groups actually created by this device.</summary>
        public int CreatedResourceGroupCount => _createdResourceGroupCount;

        protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
        {
            Interlocked.Increment(ref _createdResourceGroupCount);
            return new RecordedResourceGroup(this, descriptor);
        }
    }

    private sealed class RecordedResourceGroup : GPUResourceGroup
    {
        private readonly GPUDevice _device;

        /// <inheritdoc />
        public override IReadOnlyList<IGPUBindableResource> Resources { get; }
        protected override GPUDevice Device => _device;

        /// <summary>Captures the concrete resources bound by material assembly.</summary>
        public RecordedResourceGroup(GPUDevice device, in ResourceGroupDescriptor descriptor) : base(descriptor)
        {
            _device = device;
            IGPUBindableResource[] resources = new IGPUBindableResource[descriptor.Resources.Length];
            for (int i = 0; i < resources.Length; i++)
            {
                resources[i] = descriptor.Resources[i].Resource;
            }
            Resources = resources;
        }

        protected override void Dispose(bool disposing) { }
    }

    private static readonly FieldInfo ParameterVersionField = typeof(ShaderParameterSet)
        .GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static int ParameterVersion(GraphicsMaterial material)
        => (int)ParameterVersionField.GetValue(material.Parameters)!;

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static SlangCompilerOptions Options()
    {
        string corePath = Path.Combine(
            RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders", "Libs", "AlcoRendering_Core.slang");
        string core = File.ReadAllText(corePath);
        return new SlangCompilerOptions
        {
            Resolver = path =>
            {
                string key = SlangPathUtility.NormalizePath(path).Replace('/', '-');
                if (key.EndsWith("AlcoRendering_Core.slang", StringComparison.OrdinalIgnoreCase))
                    return core;
                if (key.EndsWith("test-variant-quad.slang", StringComparison.OrdinalIgnoreCase))
                    return QuadModule;
                if (key.EndsWith("test-variant-compute.slang", StringComparison.OrdinalIgnoreCase))
                    return ComputeModule;
                return null;
            },
        };
    }

    /// <summary>Checks compiled module variants are cached per specialization.</summary>
    [Test]
    public void Shader_CachesModulesPerSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-quad");
        ShaderModulesInfo variant0 = shader.GetShaderModules(0);
        ShaderModulesInfo variant1 = shader.GetShaderModules(1);

        Assert.Multiple(() =>
        {
            Assert.That(shader.GetShaderModules(0), Is.SameAs(variant0), "specializations cache inside the shader");
            Assert.That(shader.GetShaderModules(1), Is.SameAs(variant1));
            Assert.That(variant0, Is.Not.SameAs(variant1),
                "each specialization is its own compiled entry");
            // The module's entry points are generic (<let Flag>): it cannot link
            // unspecialized — only its argument sets are valid requests.
        });
    }

    /// <summary>Checks graphics materials reflect their construction-bound specialization.</summary>
    [Test]
    public void Graphics_MaterialIsConstructionBoundToTheSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-quad");
        ShaderModulesInfo variant1 = shader.GetShaderModules(1);

        // The variant starts at construction; SetSpecializations may switch it later.
        using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(shader, "variant_material", 1);

        Assert.Multiple(() =>
        {
            Assert.That(material.Shader, Is.SameAs(shader), "the handle is shared across variants");
            Assert.That(material.Specializations, Is.EqualTo(new[] { "1" }));
            Assert.That(material.ReflectionInfo, Is.SameAs(variant1.ReflectionInfo),
                "the material reflects its pinned variant");
        });

        // A second material of the other variant shares the handle, not the modules.
        using GraphicsMaterial other = host.RenderingSystem.CreateGraphicsMaterial(shader, "other_material", 0);
        Assert.That(other.Specializations, Is.EqualTo(new[] { "0" }));
    }

    /// <summary>Checks switching specialization invalidates a material only when arguments change.</summary>
    [Test]
    public void Graphics_Material_SwitchesSpecializationsAtRuntime()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(
            shaderSystem.GetShader("test-variant-quad"), "variant_material", 0);
        material.SetSpecializations(1);

        Assert.Multiple(() =>
        {
            Assert.That(material.Specializations, Is.EqualTo(new[] { "1" }));
            Assert.That(material.Version, Is.GreaterThan(0u), "the switch dirties the material");
        });

        // Re-setting the same arguments is a no-op.
        uint version = material.Version;
        material.SetSpecializations(1);
        Assert.That(material.Version, Is.EqualTo(version));
    }

    /// <summary>Checks graphics instances inherit their parent material's specialization.</summary>
    [Test]
    public void Graphics_Instance_InheritsTheParentSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(
            shaderSystem.GetShader("test-variant-quad"), "variant_material", 1);
        GraphicsMaterialInstance instance = material.CreateInstance();

        Assert.That(instance.Specializations, Is.EqualTo(new[] { "1" }),
            "instances resolve slots from their pinned parent variant");
    }

    /// <summary>Checks graphics material construction rejects compute-only shaders.</summary>
    [Test]
    public void Graphics_Material_RejectsComputeShaders()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader compute = shaderSystem.GetShader("test-variant-compute");

        Assert.That(() =>
        {
            using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(
                compute, "variant_material", 0);
        }, Throws.InvalidOperationException);
    }

    /// <summary>Checks compute materials and their instances retain the chosen specialization.</summary>
    [Test]
    public void Compute_MaterialIsConstructionBoundToTheSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-compute");
        ShaderModulesInfo variant1 = shader.GetShaderModules(1);

        using ComputeMaterial material = host.RenderingSystem.CreateComputeMaterial(shader, 1);
        ComputeMaterialInstance instance = material.CreateInstance();

        Assert.Multiple(() =>
        {
            Assert.That(material.Specializations, Is.EqualTo(new[] { "1" }));
            Assert.That(material.ReflectionInfo, Is.SameAs(variant1.ReflectionInfo));
            Assert.That(instance.Specializations, Is.EqualTo(new[] { "1" }));
        });
    }

    /// <summary>Checks resize invalidation through cached material instances without flushing ancestors.</summary>
    /// <param name="depth">The number of instance ancestors between the target and the root material.</param>
    /// <param name="flushAncestors">Whether ancestor groups are assembled before the target is used.</param>
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void Graphics_RenderTextureResize_InvalidatesOwnAndInheritedGroups(int depth, bool flushAncestors)
    {
        CountingNoDevice device = new();
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        RenderingSystem rendering = host.RenderingSystem;
        Shader shader = rendering.ShaderSystem.GetShaderFromModule(
            "material_resize", "material_resize.slang", ResizeModule);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment { Format = PixelFormat.RGBA8Unorm }], null));
        using RenderTexture vision = rendering.CreateRenderTexture(layout, 4, 4, "vision");
        using RenderTexture secondary = rendering.CreateRenderTexture(layout, 4, 4, "secondary");
        using GraphicsMaterial root = rendering.CreateGraphicsMaterial(shader);
        root.SetRenderTexture("visionMask", vision);
        root.SetRenderTexture("secondaryMask", secondary);
        using GraphicsMaterialInstance child = root.CreateInstance();
        using GraphicsMaterialInstance grandchild = child.CreateInstance();
        GraphicsMaterial target = depth == 0 ? root : depth == 1 ? child : grandchild;

        Assert.That(root.ReflectionInfo.GetResourceLocation(root.GetResourceId("visionMask")).GroupIndex, Is.EqualTo(0));
        Assert.That(root.ReflectionInfo.GetResourceLocation(root.GetResourceId("secondaryMask")).GroupIndex, Is.EqualTo(0),
            "Both resized slots must be validated in one group, not just the first changed slot.");
        if (flushAncestors)
        {
            root.Parameters.FlushResourceGroups();
            if (depth == 2)
            {
                child.Parameters.FlushResourceGroups();
            }
        }

        target.Parameters.FlushResourceGroups();
        GPUResourceGroup before = target.Parameters.ResourceGroups[0]!;
        GPUResourceGroup stable = target.Parameters.ResourceGroups[1]!;
        GPUTextureView oldVision = vision.ColorTextures[0].View;
        GPUTextureView oldSecondary = secondary.ColorTextures[0].View;
        Assert.That(before.Resources, Is.EqualTo(new IGPUBindableResource[] { oldVision, oldSecondary }));
        int version = ParameterVersion(root);
        int createdGroups = device.CreatedResourceGroupCount;

        vision.Resize(8, 8);
        secondary.Resize(8, 8);
        target.Parameters.FlushResourceGroups();
        GPUResourceGroup after = target.Parameters.ResourceGroups[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after.Resources, Is.EqualTo(new IGPUBindableResource[]
                { vision.ColorTextures[0].View, secondary.ColorTextures[0].View }));
            Assert.That(target.Parameters.ResourceGroups[1], Is.SameAs(stable));
            Assert.That(device.CreatedResourceGroupCount, Is.EqualTo(createdGroups + 1),
                "Only the target's changed group should be assembled, never unused ancestor groups.");
            Assert.That(ParameterVersion(root), Is.EqualTo(version + 1),
                "All changed own slots in one group must be recorded with a single invalidation.");
            if (!flushAncestors && depth > 0)
            {
                Assert.That(root.Parameters.ResourceGroups[0], Is.Null);
                Assert.That(root.Parameters.ResourceGroups[1], Is.Null);
            }
            if (!flushAncestors && depth == 2)
            {
                Assert.That(child.Parameters.ResourceGroups[0], Is.Null);
                Assert.That(child.Parameters.ResourceGroups[1], Is.Null);
            }
        });

        // A second flush must not rediscover another slot from the same resize.
        version = ParameterVersion(root);
        createdGroups = device.CreatedResourceGroupCount;
        target.Parameters.FlushResourceGroups();
        Assert.Multiple(() =>
        {
            Assert.That(ParameterVersion(root), Is.EqualTo(version));
            Assert.That(target.Parameters.ResourceGroups[0], Is.SameAs(after));
            Assert.That(device.CreatedResourceGroupCount, Is.EqualTo(createdGroups));
        });
    }

    /// <summary>Checks validation of a dirty intermediate instance's own render texture for its child.</summary>
    [Test]
    public void Graphics_RenderTextureResize_ValidatesDirtyIntermediateOwnSlots()
    {
        CountingNoDevice device = new();
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        RenderingSystem rendering = host.RenderingSystem;
        Shader shader = rendering.ShaderSystem.GetShaderFromModule(
            "material_resize", "material_resize.slang", ResizeModule);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment { Format = PixelFormat.RGBA8Unorm }], null));
        using RenderTexture vision = rendering.CreateRenderTexture(layout, 4, 4, "vision");
        using RenderTexture secondary = rendering.CreateRenderTexture(layout, 4, 4, "secondary");
        using GraphicsMaterial root = rendering.CreateGraphicsMaterial(shader);
        root.SetRenderTexture("visionMask", vision);
        using GraphicsMaterialInstance child = root.CreateInstance();
        child.SetRenderTexture("secondaryMask", secondary);
        using GraphicsMaterialInstance grandchild = child.CreateInstance();
        grandchild.Parameters.FlushResourceGroups();
        GPUResourceGroup before = grandchild.Parameters.ResourceGroups[0]!;
        int rootVersion = ParameterVersion(root);
        int childVersion = ParameterVersion(child);
        int createdGroups = device.CreatedResourceGroupCount;

        // Both owners stay dirty because only the grandchild is ever flushed.
        vision.Resize(8, 8);
        secondary.Resize(8, 8);
        grandchild.Parameters.FlushResourceGroups();
        Assert.Multiple(() =>
        {
            Assert.That(grandchild.Parameters.ResourceGroups[0], Is.Not.SameAs(before));
            Assert.That(grandchild.Parameters.ResourceGroups[0]!.Resources, Is.EqualTo(new IGPUBindableResource[]
                { vision.ColorTextures[0].View, secondary.ColorTextures[0].View }));
            Assert.That(ParameterVersion(root), Is.EqualTo(rootVersion + 1));
            Assert.That(ParameterVersion(child), Is.EqualTo(childVersion + 1));
            Assert.That(root.Parameters.ResourceGroups[0], Is.Null);
            Assert.That(child.Parameters.ResourceGroups[0], Is.Null);
            Assert.That(device.CreatedResourceGroupCount, Is.EqualTo(createdGroups + 1));
        });
        grandchild.Parameters.FlushResourceGroups();
        Assert.That(ParameterVersion(root), Is.EqualTo(rootVersion + 1));
        Assert.That(ParameterVersion(child), Is.EqualTo(childVersion + 1));
    }

    /// <summary>Checks unchanged nested instances reuse their groups with zero steady-state allocations.</summary>
    [Test]
    public void Graphics_RenderTextureResize_UnchangedChainDoesNotAllocateOrCreateGroups()
    {
        CountingNoDevice device = new();
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        RenderingSystem rendering = host.RenderingSystem;
        Shader shader = rendering.ShaderSystem.GetShaderFromModule(
            "material_resize", "material_resize.slang", ResizeModule);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment { Format = PixelFormat.RGBA8Unorm }], null));
        using RenderTexture vision = rendering.CreateRenderTexture(layout, 4, 4, "vision");
        using GraphicsMaterial root = rendering.CreateGraphicsMaterial(shader);
        root.SetRenderTexture("visionMask", vision);
        root.SetRenderTexture("secondaryMask", vision);
        using GraphicsMaterialInstance child = root.CreateInstance();
        using GraphicsMaterialInstance grandchild = child.CreateInstance();
        ShaderParameterSet parameters = grandchild.Parameters;
        for (int i = 0; i < 10; i++)
        {
            parameters.FlushResourceGroups();
        }
        GPUResourceGroup group = parameters.ResourceGroups[0]!;
        int createdGroups = device.CreatedResourceGroupCount;
        int rootVersion = ParameterVersion(root);
        int childVersion = ParameterVersion(child);
        vision.Resize(4, 4);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            parameters.FlushResourceGroups();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.Zero);
            Assert.That(parameters.ResourceGroups[0], Is.SameAs(group));
            Assert.That(device.CreatedResourceGroupCount, Is.EqualTo(createdGroups));
            Assert.That(ParameterVersion(root), Is.EqualTo(rootVersion));
            Assert.That(ParameterVersion(child), Is.EqualTo(childVersion));
            Assert.That(root.Parameters.ResourceGroups[0], Is.Null);
            Assert.That(child.Parameters.ResourceGroups[0], Is.Null);
        });
    }

    /// <summary>Checks concurrent descendants publish a shared ancestor's resize invalidation exactly once.</summary>
    [Test]
    public void Graphics_RenderTextureResize_ConcurrentChildrenValidateSharedAncestor()
    {
        CountingNoDevice device = new();
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(device: device);
        RenderingSystem rendering = host.RenderingSystem;
        Shader shader = rendering.ShaderSystem.GetShaderFromModule(
            "material_resize", "material_resize.slang", ResizeModule);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment { Format = PixelFormat.RGBA8Unorm }], null));
        using RenderTexture vision = rendering.CreateRenderTexture(layout, 4, 4, "vision");
        using GraphicsMaterial root = rendering.CreateGraphicsMaterial(shader);
        root.SetRenderTexture("visionMask", vision);
        root.SetRenderTexture("secondaryMask", vision);
        GraphicsMaterialInstance[] children = new GraphicsMaterialInstance[16];
        try
        {
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = root.CreateInstance();
                children[i].Parameters.FlushResourceGroups();
            }

            for (uint size = 5; size < 21; size++)
            {
                int version = ParameterVersion(root);
                int createdGroups = device.CreatedResourceGroupCount;
                vision.Resize(size, size);
                Parallel.For(0, children.Length, i => children[i].Parameters.FlushResourceGroups());
                Assert.That(ParameterVersion(root), Is.EqualTo(version + 1),
                    "Concurrent validators must not publish duplicate or incomplete ancestor invalidations.");
                Assert.That(device.CreatedResourceGroupCount, Is.EqualTo(createdGroups + children.Length));
                for (int i = 0; i < children.Length; i++)
                {
                    Assert.That(children[i].Parameters.ResourceGroups[0]!.Resources,
                        Is.EqualTo(new IGPUBindableResource[] { vision.ColorTextures[0].View, vision.ColorTextures[0].View }));
                }
                Assert.That(root.Parameters.ResourceGroups[0], Is.Null);
            }
        }
        finally
        {
            for (int i = 0; i < children.Length; i++)
            {
                children[i]?.Dispose();
            }
        }
    }

    /// <summary>Checks shader handles are interned independently of specialization arguments.</summary>
    [Test]
    public void ShaderSystem_InternsHandlesPerModule()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-quad");
        Assert.That(shader,
            Is.SameAs(shaderSystem.GetShader("test-variant-quad")),
            "one handle per module — variants live inside it, cached per specialization");
    }
}
