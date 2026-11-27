using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private sealed class GBufferPrograms : IDisposable
    {
        private const string SurfaceContractModule = "World3DSurface";
        private const string StandardSurfaceModule = "World3DPbrStandard";
        private const string GBufferTemplate = "World3DGBuffer";
        private const string ShadowTemplate = "World3DShadowDepth";
        private const string RsmTemplate = "World3DRsm";

        private readonly SurfaceProgramCache<World3DGBufferRoot> _gbuffer;
        private readonly SurfaceProgramCache<World3DShadowDrawRoot> _shadow;
        private readonly SurfaceProgramCache<World3DRsmDrawRoot> _rsm;

        /// <summary>Registers built-in surface and template modules and prewarms standard variants.</summary>
        public GBufferPrograms(RenderingSystem system)
        {
            ShaderSystem shaders = system.Shaders;
            ShaderCompiler compiler = shaders.Compiler;
            RegisterModule(compiler, SurfaceContractModule);
            RegisterModule(compiler, StandardSurfaceModule);
            RegisterModule(compiler, GBufferTemplate);
            RegisterModule(compiler, ShadowTemplate);
            RegisterModule(compiler, RsmTemplate);

            _gbuffer = new SurfaceProgramCache<World3DGBufferRoot>(shaders, GBufferTemplate, DepthStencilState.WriteReverseZ);
            _shadow = new SurfaceProgramCache<World3DShadowDrawRoot>(shaders, ShadowTemplate, DepthStencilState.Write);
            _rsm = new SurfaceProgramCache<World3DRsmDrawRoot>(shaders, RsmTemplate, DepthStencilState.Write);

            _gbuffer.WarmStandard();
            _shadow.WarmStandard();
            _rsm.WarmStandard();
        }

        /// <summary>Records one GBuffer indirect draw packet for a bucket chunk.</summary>
        public void Draw(RenderContext.RenderScope pass, string? surfaceModule, bool alphaTest, CullMode cull,
            GPUBufferView directory, GPUBufferView views, GPUBufferView drawRefs, GpuBuffer geometryPage,
            uint firstDraw, BufferSlice args, uint count, uint viewIndex = 0)
        {
            Material material = _gbuffer.GetMaterial(surfaceModule ?? StandardSurfaceModule, alphaTest, cull);
            World3DGBufferRoot root = new(directory, views, drawRefs, geometryPage.View, firstDraw, viewIndex);
            pass.DrawIndexedIndirect(material, in root, material.DepthStencilState, geometryPage.Slice(), IndexType.UInt32, args, count);
        }

        /// <summary>Records one shadow-depth indirect draw packet for a shadow view.</summary>
        public void DrawShadow(RenderContext.RenderScope pass, string? surfaceModule, bool alphaTest, CullMode cull,
            GPUBufferView directory, GPUBufferView views, GPUBufferView drawRefs, GpuBuffer geometryPage,
            uint firstDraw, BufferSlice args, uint count, uint viewIndex)
        {
            Material material = _shadow.GetMaterial(surfaceModule ?? StandardSurfaceModule, alphaTest, cull);
            World3DShadowDrawRoot root = new(directory, views, drawRefs, geometryPage.View, firstDraw, viewIndex);
            pass.DrawIndexedIndirect(material, in root, material.DepthStencilState, geometryPage.Slice(), IndexType.UInt32, args, count);
        }

        /// <summary>Records one RSM indirect draw packet for an RSM capture pass.</summary>
        public void DrawRsm(RenderContext.RenderScope pass, string? surfaceModule, bool alphaTest, CullMode cull,
            GPUBufferView directory, GPUBufferView views, GPUBufferView drawRefs, GpuBuffer geometryPage,
            uint firstDraw, BufferSlice args, uint count, uint viewIndex,
            Vector4 sunDirectionAndIntensity, Vector4 sunColorAndPadding)
        {
            Material material = _rsm.GetMaterial(surfaceModule ?? StandardSurfaceModule, alphaTest, cull);
            World3DRsmDrawRoot root = new(directory, views, drawRefs, geometryPage.View, firstDraw, viewIndex, 0, 0,
                sunDirectionAndIntensity, sunColorAndPadding);
            pass.DrawIndexedIndirect(material, in root, material.DepthStencilState, geometryPage.Slice(), IndexType.UInt32, args, count);
        }

        /// <summary>Releases cached materials; the shader system owns the composed shaders.</summary>
        public void Dispose()
        {
            _gbuffer.Dispose();
            _shadow.Dispose();
            _rsm.Dispose();
        }

        private static void RegisterModule(ShaderCompiler compiler, string name) =>
            compiler.RegisterVirtualModule(name, name + ".slang", ReadWorldShader(name));

        private sealed class SurfaceProgramCache<TRoot>(ShaderSystem shaders, string templateModule, DepthStencilState depthStencil)
            : IDisposable where TRoot : unmanaged
        {
            private readonly ShaderSystem _shaders = shaders;
            private readonly string _templateModule = templateModule;
            private readonly DepthStencilState _depthStencil = depthStencil;
            private readonly Dictionary<(string SurfaceModule, bool AlphaTest, CullMode Cull), Material> _materials = [];

            public void WarmStandard()
            {
                GetMaterial(StandardSurfaceModule, alphaTest: false, CullMode.CounterClockwise);
                GetMaterial(StandardSurfaceModule, alphaTest: true, CullMode.CounterClockwise);
            }

            public Material GetMaterial(string surfaceModule, bool alphaTest, CullMode cull)
            {
                var key = (surfaceModule, alphaTest, cull);
                if (_materials.TryGetValue(key, out Material? material))
                    return material;

                Shader shader = _shaders.Compose(_templateModule, surfaceModule, ShaderKind.Graphics);
                material = shader.CreateMaterial($"{shader.Name}:{(alphaTest ? "Mask" : "Opaque")}:{cull}", alphaTest);
                ValidateRoot<TRoot>(material.Reflection.Root
                    ?? throw new InvalidOperationException($"{material.Name} did not reflect a root layout."));
                material.Rasterization = new RasterizationState { Cull = cull, Fill = FillMode.Solid };
                material.DepthStencilState = _depthStencil;
                _materials.Add(key, material);
                return material;
            }

            public void Dispose() => _materials.Clear();
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private readonly record struct World3DGBufferRoot(
        [field: FieldOffset(0)] GPUBufferView Directory,
        [field: FieldOffset(8)] GPUBufferView Views,
        [field: FieldOffset(16)] GPUBufferView DrawRefs,
        [field: FieldOffset(24)] GPUBufferView PayloadPage,
        [field: FieldOffset(32)] uint FirstDraw,
        [field: FieldOffset(36)] uint ViewIndex);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private readonly record struct World3DShadowDrawRoot(
        [field: FieldOffset(0)] GPUBufferView Directory,
        [field: FieldOffset(8)] GPUBufferView Views,
        [field: FieldOffset(16)] GPUBufferView DrawRefs,
        [field: FieldOffset(24)] GPUBufferView PayloadPage,
        [field: FieldOffset(32)] uint FirstDraw,
        [field: FieldOffset(36)] uint ViewIndex);

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    private readonly record struct World3DRsmDrawRoot(
        [field: FieldOffset(0)] GPUBufferView Directory,
        [field: FieldOffset(8)] GPUBufferView Views,
        [field: FieldOffset(16)] GPUBufferView DrawRefs,
        [field: FieldOffset(24)] GPUBufferView PayloadPage,
        [field: FieldOffset(32)] uint FirstDraw,
        [field: FieldOffset(36)] uint ViewIndex,
        [field: FieldOffset(40)] uint Reserved0,
        [field: FieldOffset(44)] uint Reserved1,
        [field: FieldOffset(48)] Vector4 SunDirectionAndIntensity,
        [field: FieldOffset(64)] Vector4 SunColorAndPadding);
}
