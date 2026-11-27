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
        private const string TemplateModule = "World3DGBuffer";

        private readonly ShaderSystem _shaders;
        private readonly Dictionary<(string SurfaceModule, bool AlphaTest, CullMode Cull), Material> _materials = [];

        /// <summary>Registers the built-in surface modules and validates the standard GBuffer variants.</summary>
        public GBufferPrograms(RenderingSystem system)
        {
            _shaders = system.Shaders;
            ShaderCompiler compiler = _shaders.Compiler;
            RegisterModule(compiler, SurfaceContractModule);
            RegisterModule(compiler, StandardSurfaceModule);
            RegisterModule(compiler, TemplateModule);

            GetMaterial(StandardSurfaceModule, alphaTest: false, CullMode.CounterClockwise);
            GetMaterial(StandardSurfaceModule, alphaTest: true, CullMode.CounterClockwise);
        }

        /// <summary>Records one packet of indexed indirect draws from the bound geometry payload page.</summary>
        public void Draw(RenderContext.RenderScope pass, string? surfaceModule, bool alphaTest, CullMode cull,
            GPUBufferView directory, GPUBufferView views, GPUBufferView drawRefs, GpuBuffer geometryPage,
            uint firstDraw, BufferSlice args, uint count)
        {
            Material material = GetMaterial(surfaceModule ?? StandardSurfaceModule, alphaTest, cull);
            World3DGBufferRoot root = new(directory, views, drawRefs, geometryPage.View, firstDraw, ViewIndex: 0);
            pass.DrawIndexedIndirect(material, in root, material.DepthStencilState, geometryPage.Slice(), IndexType.UInt32, args, count);
        }

        /// <summary>Releases the cached materials; the shader system owns the composed programs.</summary>
        public void Dispose() => _materials.Clear();

        private static void RegisterModule(ShaderCompiler compiler, string name) =>
            compiler.RegisterVirtualModule(name, name + ".slang", ReadWorldShader(name));

        private Material GetMaterial(string surfaceModule, bool alphaTest, CullMode cull)
        {
            var key = (surfaceModule, alphaTest, cull);
            if (_materials.TryGetValue(key, out Material? material))
                return material;

            Shader shader = _shaders.Compose(TemplateModule, surfaceModule, ShaderKind.Graphics);
            material = shader.CreateMaterial($"{shader.Name}:{(alphaTest ? "Mask" : "Opaque")}:{cull}", alphaTest);
            ValidateRoot<World3DGBufferRoot>(material.Reflection.Root
                ?? throw new InvalidOperationException($"{material.Name} did not reflect a GBuffer root layout."));
            material.Rasterization = new RasterizationState { Cull = cull, Fill = FillMode.Solid };
            material.DepthStencilState = DepthStencilState.WriteReverseZ;
            _materials.Add(key, material);
            return material;
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
}
