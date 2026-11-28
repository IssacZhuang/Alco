using System.Reflection;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>
/// Compiles all 51 embedded Alco.World3D shader modules and composed ISurface variants across Spirv, SpirvCompact, Dxil, and MetalSource.
/// </summary>
[Category("NoGPU")]
[TestFixture]
public sealed class ValidateShader
{
    private const int ExpectedEmbeddedShaderCount = 51;

    private static readonly string[] s_libraryModules =
    [
        "World3DSceneData",
        "World3DSurface",
        "World3DPbrStandard",
        "World3DAtmosphere",
        "World3DOcclusionCommon",
        "World3DShadowSample",
        "World3DVoxelCommon",
        "World3DCloudCommon",
    ];

    private static readonly (string Module, uint RootBytes)[] s_computeKernels =
    [
        // Deferred & Geometry
        ("World3DCullSelect", 100u),
        ("World3DBuildArgs", 72u),
        ("World3DDeferredLighting", 176u),
        // Occlusion & Shadows
        ("World3DHiZBuild", 32u),
        ("World3DOcclusionPrepare", 64u),
        ("World3DOcclusionRetest", 60u),
        ("World3DShadowMinMaxReduce", 24u),
        ("World3DShadowMinMaxDilate", 24u),
        ("World3DFarHeightBuild", 48u),
        // VoxelGI
        ("World3DVoxelInit", 80u),
        ("World3DVoxelBlueNoise", 16u),
        ("World3DVoxelPageManage", 16u),
        ("World3DVoxelCullInstances", 96u),
        ("World3DVoxelPrepareArgs", 96u),
        ("World3DVoxelExpandChunks", 32u),
        ("World3DVoxelClear", 44u),
        ("World3DVoxelInject", 48u),
        ("World3DVoxelMip", 24u),
        ("World3DVoxelMipChain", 32u),
        ("World3DVoxelPropagate", 88u),
        ("World3DVoxelTrace", 64u),
        ("World3DVoxelDemosaic", 80u),
        ("World3DVoxelUpsample", 48u),
        // Volumetric Clouds & Light
        ("World3DCloudNoise", 20u),
        ("World3DCloudShadow", 36u),
        ("World3DCloudMarch", 40u),
        ("World3DCloudTemporal", 48u),
        ("World3DCloudComposite", 48u),
        ("World3DVolumetricLightMarch", 112u),
        ("World3DVolumetricLightComposite", 40u),
        // Forward Sorting
        ("World3DForwardCullKey", 64u),
        ("World3DForwardSort", 32u),
        ("World3DForwardEmitArgs", 40u),
        ("World3DForwardSnapshot", 24u),
        // SSR
        ("World3DSsrBlueNoise", 20u),
        ("World3DSsrTrace", 96u),
        ("World3DSsrResolve", 52u),
        ("World3DSsrComposite", 80u),
    ];

    /// <summary>Verifies that all 51 embedded shaders exist without duplicates and are covered by validation.</summary>
    [Test]
    public void EmbeddedShaderManifest_MatchesExpectedSet()
    {
        Assembly assembly = typeof(World3DScene).Assembly;
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(resources.Length, Is.EqualTo(ExpectedEmbeddedShaderCount));
        Assert.That(resources.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(ExpectedEmbeddedShaderCount));

        HashSet<string> expected = new(StringComparer.Ordinal);
        expected.UnionWith(s_libraryModules);
        foreach ((string kernel, _) in s_computeKernels)
            expected.Add(kernel);
        expected.Add("World3DGBuffer");
        expected.Add("World3DShadowDepth");
        expected.Add("World3DRsm");
        expected.Add("World3DVoxelVoxelize");
        expected.Add("World3DForward");

        Assert.That(expected.Count, Is.EqualTo(ExpectedEmbeddedShaderCount));
        foreach (string resource in resources)
        {
            string stem = resource[..^".slang".Length];
            Assert.That(expected.Contains(stem), Is.True, $"Embedded shader '{resource}' is missing from ValidateShader inventory.");
        }
    }

    /// <summary>Compiles every World3D module, reflected ABI struct, compute kernel, and composed template on each target.</summary>
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ValidateAll51ModulesAndComposedSurfaces(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
            Assert.Ignore($"The bundled shader compiler cannot emit {target} on this host.");

        using ShaderCompiler compiler = new(target, new ShaderCompilerOptions { SearchPaths = [] });
        RegisterAllWorld3DModules(compiler);

        string customSurfacePath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Assets", "Shaders", "Materials", "ParameterizedSurface.slang");
        Assert.That(File.Exists(customSurfacePath), Is.True, $"Missing test surface shader '{customSurfacePath}'.");
        compiler.RegisterVirtualModule("ParameterizedSurface", "ParameterizedSurface.slang", File.ReadAllText(customSurfacePath));

        ValidateSharedStructReflection(compiler);

        foreach (string library in s_libraryModules)
        {
            ShaderModule module = compiler.LoadModule(library);
            Assert.That(module, Is.Not.Null, library);
        }

        foreach ((string kernel, uint rootBytes) in s_computeKernels)
        {
            CompiledShader compiled = compiler.Compile(kernel, ShaderPipelineKind.Compute);
            ShaderRootLayout root = compiled.Reflection.Root
                ?? throw new AssertionException($"Kernel '{kernel}' ({target}) did not reflect a [GPURoot].");
            Assert.That(root.SizeBytes, Is.EqualTo(rootBytes), $"{kernel} ({target}) root size");
            Assert.That(root.Fields, Is.Not.Empty, $"{kernel} ({target}) root fields");
        }

        ReadOnlySpan<string> surfaces = ["World3DPbrStandard", "ParameterizedSurface"];
        ReadOnlySpan<string> alphaSpecs = ["false", "true"];
        (string Template, uint RootBytes)[] rasterTemplates =
        [
            ("World3DGBuffer", 40u),
            ("World3DShadowDepth", 40u),
            ("World3DRsm", 80u),
        ];

        foreach (string surface in surfaces)
        {
            foreach ((string template, uint rootBytes) in rasterTemplates)
            {
                foreach (string alphaTest in alphaSpecs)
                {
                    CompiledShader compiled = compiler.CompileComposed(template, surface, ShaderPipelineKind.Graphics,
                        new ShaderCompileOptions { SpecializationArgs = [alphaTest] });
                    ShaderRootLayout root = compiled.Reflection.Root
                        ?? throw new AssertionException($"{template}+{surface} ({target}) missing [GPURoot].");
                    Assert.That(root.SizeBytes, Is.EqualTo(rootBytes), $"{template}+{surface} ({target}, alphaTest={alphaTest})");
                }
            }

            CompiledShader voxelize = compiler.CompileComposed("World3DVoxelVoxelize", surface, ShaderPipelineKind.Compute);
            Assert.That(voxelize.Reflection.Root?.SizeBytes, Is.EqualTo(136u), $"World3DVoxelVoxelize+{surface} ({target})");

            CompiledShader forward = compiler.CompileComposed("World3DForward", surface, ShaderPipelineKind.Graphics);
            Assert.That(forward.Reflection.Root?.SizeBytes, Is.EqualTo(128u), $"World3DForward+{surface} ({target})");
        }
    }

    private static void RegisterAllWorld3DModules(ShaderCompiler compiler)
    {
        Assembly assembly = typeof(World3DScene).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
                continue;
            string moduleName = resource[..^".slang".Length];
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);
            compiler.RegisterVirtualModule(moduleName, resource, reader.ReadToEnd());
        }
    }

    private static void ValidateSharedStructReflection(ShaderCompiler compiler)
    {
        IReadOnlyList<ShaderTypeLayout> sceneTypes = compiler.LoadModule("World3DSceneData").GetTypes();
        AssertStruct(sceneTypes, "World3DInstanceSpatialGpu", 112u, "ObjectToCell", rowMajor: true);
        AssertStruct(sceneTypes, "World3DView", 192u, "CameraVP", rowMajor: true);
        AssertStruct(sceneTypes, "World3DCullViewGpu", 128u, "ViewProj", rowMajor: true);
        AssertStruct(sceneTypes, "World3DOccludedCandidate", 8u);
        AssertStruct(sceneTypes, "World3DPointLightGpu", 48u);
        AssertStruct(sceneTypes, "World3DInstanceGpu", 128u);
        AssertStruct(sceneTypes, "World3DMaterialGpu", 64u);
        AssertStruct(sceneTypes, "World3DGeometryLodGpu", 64u);
        AssertStruct(sceneTypes, "World3DVertexGpu", 48u);

        IReadOnlyList<ShaderTypeLayout> shadowTypes = compiler.LoadModule("World3DShadowSample").GetTypes();
        AssertStruct(shadowTypes, "World3DShadowFrameGpu", 672u);

        IReadOnlyList<ShaderTypeLayout> voxelTypes = compiler.LoadModule("World3DVoxelCommon").GetTypes();
        AssertStruct(voxelTypes, "World3DVoxelCascadeGpu", 32u);
        AssertStruct(voxelTypes, "World3DVoxelEvictBoxGpu", 32u);
        AssertStruct(voxelTypes, "World3DVoxelCandidateGpu", 16u);
        AssertStruct(voxelTypes, "World3DVoxelChunkGpu", 8u);
        AssertStruct(voxelTypes, "World3DVoxelCountersGpu", 40u);
        AssertStruct(voxelTypes, "World3DVoxelFrameGpu", 496u);

        IReadOnlyList<ShaderTypeLayout> cloudTypes = compiler.LoadModule("World3DCloudCommon").GetTypes();
        AssertStruct(cloudTypes, "World3DCloudFrameGpu", 176u);
    }

    private static void AssertStruct(IReadOnlyList<ShaderTypeLayout> types, string name, uint expectedBytes,
        string? matrixField = null, bool rowMajor = false)
    {
        ShaderTypeLayout? layout = types.FirstOrDefault(t => t.TypeName == name);
        Assert.That(layout, Is.Not.Null, $"Missing reflected struct '{name}'.");
        Assert.That(layout!.SizeBytes, Is.EqualTo(expectedBytes), $"{name}.SizeBytes");
        if (matrixField != null && rowMajor)
        {
            ShaderFieldLayout? field = layout.FindField(matrixField);
            Assert.That(field, Is.Not.Null, $"{name}.{matrixField}");
            Assert.That(field!.Type.MatrixLayout, Is.EqualTo(ShaderMatrixLayout.RowMajor), $"{name}.{matrixField}");
        }
    }
}
