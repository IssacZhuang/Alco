using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>The compiled artifacts of one Slang graphics-shader permutation.</summary>
/// <param name="VertexSpirv">The vertex-stage SPIR-V module.</param>
/// <param name="FragmentSpirv">The fragment-stage SPIR-V module.</param>
/// <param name="Reflection">The engine reflection info built from Slang reflection.</param>
/// <param name="UniformMembers">The requested uniform blocks' member layouts, by block name.</param>
public sealed record SlangCompiledShader(
    byte[] VertexSpirv,
    byte[] FragmentSpirv,
    ShaderReflectionInfo Reflection,
    IReadOnlyDictionary<string, List<SlangUniformMember>> UniformMembers);

/// <summary>
/// Compiles World3D shaders to SPIR-V with the Slang front end and reflects
/// them with Slang's own reflection API, bypassing the engine's DXC +
/// SPIR-V-reflect toolchain entirely:
/// <list type="bullet">
/// <item>Slang material wrappers - a pass template instantiated for a surface
/// module (see <see cref="MaterialCompiler"/>); one compile contains the
/// vertex and pixel entry points so both stages share a single program
/// layout.</item>
/// <item>Engine pipeline shaders - the HLSL sources under
/// <c>Assets/Shaders</c> (GBuffer.hlsl, VoxelClear.hlsl, ...) compiled as-is;
/// they are Slang-compatible through the <c>__SLANG__</c> guard in
/// Core.hlsli, with the engine's depth-texture and entry-point conventions
/// applied on top.</item>
/// </list>
/// A Slang session is not thread-safe; all compiles serialize on a lock,
/// mirroring how the engine serializes DXC compiles.
/// </summary>
internal sealed class SlangShaderCompiler : IDisposable
{
    private readonly Lock _lock = new();
    private IntPtr _session;

    /// <summary>
    /// Compile one graphics permutation: the wrapper source (a translation unit
    /// that #includes the pass template and imports the surface module - see
    /// <see cref="MaterialCompiler"/>) with preprocessor defines, resolving
    /// every module/import/include through the supplied file resolver.
    /// </summary>
    /// <param name="name">The shader name (diagnostics and the virtual source path).</param>
    /// <param name="source">The generated wrapper source.</param>
    /// <param name="defines">Preprocessor defines applied to the wrapper translation unit.</param>
    /// <param name="uniformBlocks">Uniform block names whose member layouts to extract.</param>
    /// <param name="fileResolver">Serves module/include paths.</param>
    /// <returns>The SPIR-V modules with reflection data.</returns>
    public SlangCompiledShader CompileGraphics(
        string name,
        string source,
        IReadOnlyList<(string Name, string Value)> defines,
        string[] uniformBlocks,
        SlangFileResolver fileResolver)
    {
        EnsureSession();
        using (_lock.EnterScope())
        {
            SlangFileSystem fileSystem = new(fileResolver);
            IntPtr request = SlangNative.spCreateCompileRequest(_session);
            if (request == IntPtr.Zero)
            {
                fileSystem.Dispose();
                throw new InvalidOperationException("Slang failed to create a compile request.");
            }
            try
            {
                SlangNative.spSetFileSystem(request, fileSystem.Pointer);
                SlangNative.spSetCodeGenTarget(request, SlangNative.SLANG_SPIRV);
                SlangNative.spSetOptimizationLevel(
                    request, SlangNative.SLANG_OPTIMIZATION_LEVEL_MAXIMAL);
                SlangNative.spSetMatrixLayoutMode(
                    request, SlangNative.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR);
                ApplyCompilerOptions(request, name);

                int translationUnit = SlangNative.spAddTranslationUnit(
                    request, SlangNative.SLANG_SOURCE_LANGUAGE_SLANG, name);
                SlangNative.spAddTranslationUnitSourceString(
                    request, translationUnit, $"{name}.slang",
                    System.Text.Encoding.UTF8.GetBytes(source + "\0"));
                for (int i = 0; i < defines.Count; i++)
                {
                    SlangNative.spTranslationUnit_addPreprocessorDefine(
                        request, translationUnit, defines[i].Name, defines[i].Value);
                }

                int vertexEntry = SlangNative.spAddEntryPoint(
                    request, translationUnit, "MainVS", SlangNative.SLANG_STAGE_VERTEX);
                int fragmentEntry = SlangNative.spAddEntryPoint(
                    request, translationUnit, "MainPS", SlangNative.SLANG_STAGE_FRAGMENT);

                int result = SlangNative.spCompile(request);
                if (result != SlangNative.SLANG_OK)
                {
                    string diagnostics = SlangNative.StringFromPtr(
                        SlangNative.spGetDiagnosticOutput(request)) ?? "unknown Slang error";
                    throw new ShaderValidationException($"Slang compilation of '{name}' failed:\n{diagnostics}");
                }

                byte[] vertexSpirv = ReadEntryPointCode(request, vertexEntry, name, "vertex");
                byte[] fragmentSpirv = ReadEntryPointCode(request, fragmentEntry, name, "fragment");

                IntPtr reflection = SlangNative.spGetReflection(request);
                if (reflection == IntPtr.Zero)
                {
                    throw new ShaderValidationException($"Slang compilation of '{name}' produced no reflection.");
                }

                ShaderReflectionInfo reflectionInfo = SlangReflection.BuildReflectionInfo(reflection);
                Dictionary<string, List<SlangUniformMember>> members = new(StringComparer.Ordinal);
                foreach (string block in uniformBlocks)
                {
                    members[block] = SlangReflection.GetUniformMembers(reflection, block);
                }
                return new SlangCompiledShader(vertexSpirv, fragmentSpirv, reflectionInfo, members);
            }
            finally
            {
                // The request keeps a reference to the file system: destroy it first.
                SlangNative.spDestroyCompileRequest(request);
                fileSystem.Dispose();
            }
        }
    }

    /// <summary>
    /// Compile one engine pipeline shader (the HLSL sources under
    /// <c>Assets/Shaders</c>, e.g. GBuffer.hlsl or VoxelClear.hlsl) through the
    /// Slang front end instead of the engine's DXC toolchain. The sources are
    /// Slang-compatible as-is (see the <c>__SLANG__</c> guard in
    /// Core.hlsli); entry points are discovered with the engine's own
    /// attribute scan, so the same file must contain either a vertex+pixel
    /// pair or a single compute entry, exactly like the DXC path.
    /// </summary>
    /// <param name="name">The shader name (diagnostics; usually the asset path).</param>
    /// <param name="source">The HLSL source text.</param>
    /// <param name="defines">Preprocessor defines (name only, defined as 1).</param>
    /// <param name="fileResolver">Serves include paths ("Shaders/...").</param>
    /// <returns>The engine modules info with Slang-reflected layout.</returns>
    public ShaderModulesInfo CompileEngineShader(
        string name,
        string source,
        IReadOnlyList<string> defines,
        SlangFileResolver fileResolver)
    {
        // Entry discovery: identical scan to the engine's HLSL pipeline
        // (ShaderUtility.CompileHLSL), over the same source text.
        string? vertexName = null;
        string? fragmentName = null;
        string? computeName = null;
        foreach (HlslFunctionInfo function in ShaderUtility.GetHLSLFunctionInfo(source))
        {
            if (function.Stage.HasFlag(ShaderStage.Vertex) && vertexName == null)
            {
                vertexName = function.Name;
            }
            if (function.Stage.HasFlag(ShaderStage.Fragment) && fragmentName == null)
            {
                fragmentName = function.Name;
            }
            if (function.Stage.HasFlag(ShaderStage.Compute) && computeName == null)
            {
                computeName = function.Name;
            }
        }

        bool graphics = vertexName != null && fragmentName != null;
        bool compute = computeName != null;
        if (!graphics && !compute)
        {
            throw new ShaderValidationException($"No entry point defined in the Slang pipeline shader '{name}'.");
        }
        if (graphics && compute)
        {
            throw new ShaderValidationException(
                $"No compute entry point is allowed alongside vertex/pixel in the Slang pipeline shader '{name}'.");
        }

        // Depth textures: the same source-macro conventions and SPIR-V rewrite
        // the DXC path applies (DEFINE_TEX2D_DEPTH / DEFINE_TEX2D_DEPTH_SAMPLE).
        List<string> depthTextureNames = [];
        List<string> comparisonSamplerNames = [];
        foreach (Match match in ShaderUtility.RegexDepthTexture.Matches(source))
        {
            depthTextureNames.Add(match.Groups[1].Value);
        }
        foreach (Match match in ShaderUtility.RegexDepthTextureSample.Matches(source))
        {
            string textureName = match.Groups[1].Value;
            depthTextureNames.Add(textureName);
            comparisonSamplerNames.Add(textureName + "Sampler");
        }
        IReadOnlyDictionary<string, PixelFormat> declaredStorageFormats =
            FindDeclaredStorageFormats(source);
        IReadOnlyDictionary<string, SlangBindingRemapper.Location> sourceResourceLayout =
            SlangBindingRemapper.ParseSourceLayout(source);

        EnsureSession();
        using (_lock.EnterScope())
        {
            SlangFileSystem fileSystem = new(fileResolver);
            IntPtr request = SlangNative.spCreateCompileRequest(_session);
            if (request == IntPtr.Zero)
            {
                fileSystem.Dispose();
                throw new InvalidOperationException("Slang failed to create a compile request.");
            }
            try
            {
                SlangNative.spSetFileSystem(request, fileSystem.Pointer);
                SlangNative.spSetCodeGenTarget(request, SlangNative.SLANG_SPIRV);
                SlangNative.spSetOptimizationLevel(
                    request, SlangNative.SLANG_OPTIMIZATION_LEVEL_MAXIMAL);
                SlangNative.spSetMatrixLayoutMode(
                    request, SlangNative.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR);
                ApplyCompilerOptions(request, name);

                int translationUnit = SlangNative.spAddTranslationUnit(
                    request, SlangNative.SLANG_SOURCE_LANGUAGE_SLANG, name);
                SlangNative.spAddTranslationUnitSourceString(request, translationUnit, name,
                    System.Text.Encoding.UTF8.GetBytes(source + "\0"));
                for (int i = 0; i < defines.Count; i++)
                {
                    SlangNative.spTranslationUnit_addPreprocessorDefine(
                        request, translationUnit, defines[i], ShaderUtility.DefineTrue);
                }

                // Slang names every SPIR-V entry point "main" in the generated
                // module regardless of the source function name.
                int vertexEntry = -1;
                int fragmentEntry = -1;
                int computeEntry = -1;
                if (graphics)
                {
                    vertexEntry = SlangNative.spAddEntryPoint(
                        request, translationUnit, vertexName!, SlangNative.SLANG_STAGE_VERTEX);
                    fragmentEntry = SlangNative.spAddEntryPoint(
                        request, translationUnit, fragmentName!, SlangNative.SLANG_STAGE_FRAGMENT);
                }
                else
                {
                    computeEntry = SlangNative.spAddEntryPoint(
                        request, translationUnit, computeName!, SlangNative.SLANG_STAGE_COMPUTE);
                }

                int result = SlangNative.spCompile(request);
                if (result != SlangNative.SLANG_OK)
                {
                    string diagnostics = SlangNative.StringFromPtr(
                        SlangNative.spGetDiagnosticOutput(request)) ?? "unknown Slang error";
                    throw new ShaderValidationException($"Slang compilation of '{name}' failed:\n{diagnostics}");
                }

                byte[] vertexSpirv = [];
                byte[] fragmentSpirv = [];
                byte[] computeSpirv = [];
                if (graphics)
                {
                    vertexSpirv = ReadEntryPointCode(request, vertexEntry, name, "vertex");
                    fragmentSpirv = ReadEntryPointCode(request, fragmentEntry, name, "fragment");
                }
                else
                {
                    computeSpirv = ReadEntryPointCode(request, computeEntry, name, "compute");
                }

                // Depth textures become true depth images in the SPIR-V itself
                // (wgpu validates the image type against the depth binding).
                // BaseInstance zeroing already happened in ReadEntryPointCode.
                byte[][] modules = graphics ? [vertexSpirv, fragmentSpirv] : [computeSpirv];
                IntPtr reflection = SlangNative.spGetReflection(request);
                if (reflection == IntPtr.Zero)
                {
                    throw new ShaderValidationException($"Slang compilation of '{name}' produced no reflection.");
                }

                ThreadGroupSize? threadGroupSize = compute
                    ? SlangSpirvFacts.ReadThreadGroupSize(computeSpirv)
                    : null;
                ShaderReflectionInfo originalReflectionInfo = SlangReflection.BuildReflectionInfo(
                    reflection,
                    threadGroupSize,
                    variableName => LookupStorageFormat(modules, variableName)
                        ?? LookupDeclaredStorageFormat(declaredStorageFormats, variableName));
                for (int i = 0; i < modules.Length; i++)
                {
                    modules[i] = SlangBindingRemapper.RemapSpirv(
                        modules[i], originalReflectionInfo, sourceResourceLayout);
                }
                vertexSpirv = graphics ? modules[0] : [];
                fragmentSpirv = graphics ? modules[1] : [];
                computeSpirv = compute ? modules[0] : [];
                ShaderReflectionInfo reflectionInfo = SlangBindingRemapper.RemapReflection(
                    originalReflectionInfo, sourceResourceLayout);
                IReadOnlyDictionary<(uint Set, uint Binding), string> depthTextureBindings =
                    FindResourceBindings(reflectionInfo, depthTextureNames);

                if (depthTextureNames.Count > 0)
                {
                    for (int i = 0; i < modules.Length; i++)
                    {
                        modules[i] = SlangDepthTexturePatcher.MarkDepthTextures(
                            modules[i], depthTextureBindings);
                    }
                    vertexSpirv = modules[0];
                    fragmentSpirv = modules.Length > 1 ? modules[1] : [];
                    computeSpirv = graphics ? [] : modules[0];
                }

                SlangReflection.MarkDepthTextures(reflectionInfo, depthTextureNames, comparisonSamplerNames);

                if (graphics)
                {
                    return ShaderModulesInfo.CreateGraphics(
                        name,
                        [.. defines],
                        new ShaderModule(ShaderStage.Vertex, ShaderLanguage.SPIRV, vertexSpirv, "main"),
                        new ShaderModule(ShaderStage.Fragment, ShaderLanguage.SPIRV, fragmentSpirv, "main"),
                        reflectionInfo);
                }
                return ShaderModulesInfo.CreateCompute(
                    name,
                    [.. defines],
                    new ShaderModule(ShaderStage.Compute, ShaderLanguage.SPIRV, computeSpirv, "main"),
                    reflectionInfo);
            }
            finally
            {
                // The request keeps a reference to the file system: destroy it first.
                SlangNative.spDestroyCompileRequest(request);
                fileSystem.Dispose();
            }
        }
    }

    /// <summary>
    /// Find a storage image's declared format across the compiled stage
    /// modules; the variable is only declared in stages that reference it.
    /// </summary>
    private static PixelFormat? LookupStorageFormat(byte[][] modules, string variableName)
    {
        foreach (byte[] module in modules)
        {
            if (SlangSpirvFacts.TryReadStorageImageFormat(module, variableName, out PixelFormat format))
            {
                return format;
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, PixelFormat> FindDeclaredStorageFormats(string source)
    {
        const string pattern = @"DEFINE_TEX(?:2D|3D)_STORAGE\s*\(\s*\d+\s*,\s*"
            + @"([A-Za-z_][A-Za-z0-9_]*)\s*,\s*[^,]+\s*,\s*""([^""]+)""\s*\)";
        Dictionary<string, PixelFormat> formats = [];
        foreach (Match match in Regex.Matches(source, pattern, RegexOptions.CultureInvariant))
        {
            string name = match.Groups[1].Value;
            string formatName = match.Groups[2].Value;
            formats[name] = formatName switch
            {
                "rgba8" => PixelFormat.RGBA8Unorm,
                "rgba8_snorm" => PixelFormat.RGBA8Snorm,
                "rgba16f" => PixelFormat.RGBA16Float,
                "rgba32f" => PixelFormat.RGBA32Float,
                "r32f" => PixelFormat.R32Float,
                "rgba32ui" => PixelFormat.RGBA32Uint,
                _ => throw new ShaderValidationException(
                    $"Slang pipeline shader declares unsupported storage image format '{formatName}' for '{name}'."),
            };
        }
        return formats;
    }

    private static PixelFormat? LookupDeclaredStorageFormat(
        IReadOnlyDictionary<string, PixelFormat> formats,
        string variableName)
    {
        return formats.TryGetValue(variableName, out PixelFormat format) ? format : null;
    }

    private static IReadOnlyDictionary<(uint Set, uint Binding), string> FindResourceBindings(
        ShaderReflectionInfo reflection,
        IReadOnlyCollection<string> resourceNames)
    {
        Dictionary<(uint Set, uint Binding), string> bindings = [];
        foreach (string name in resourceNames.Distinct())
        {
            if (!reflection.TryGetResourceLocation(name, out ShaderResourceLocation location))
            {
                continue;
            }

            uint set = reflection.BindGroups[location.GroupIndex].Group;
            bindings.Add((set, location.Binding), name);
        }
        return bindings;
    }

    private static void ApplyCompilerOptions(IntPtr request, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        // Slang's GLSL/glslang SPIR-V route avoids pathological direct-backend
        // modules in the heavier World3D shaders. Blue-noise generation is the
        // one exception: its glslang module is rejected by wgpu's validator,
        // while Slang's direct SPIR-V module validates and renders correctly.
        if (stem.Equals("ScreenSpaceReflectionBlueNoise", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IntPtr argument = Marshal.StringToCoTaskMemUTF8("-emit-spirv-via-glsl");
        try
        {
            int result = SlangNative.spProcessCommandLineArguments(request, [argument], 1);
            if (result != SlangNative.SLANG_OK)
            {
                string diagnostics = SlangNative.StringFromPtr(
                    SlangNative.spGetDiagnosticOutput(request)) ?? "unknown Slang error";
                throw new ShaderValidationException(
                    $"Slang rejected -emit-spirv-via-glsl:\n{diagnostics}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(argument);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_session != IntPtr.Zero)
        {
            SlangNative.spDestroySession(_session);
            _session = IntPtr.Zero;
        }
    }

    private void EnsureSession()
    {
        if (_session != IntPtr.Zero)
        {
            return;
        }
        _session = SlangNative.spCreateSession();
        if (_session == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Failed to create a Slang session; the slang native libraries are missing from the output directory.");
        }
    }

    private static byte[] ReadEntryPointCode(IntPtr request, int entryPointIndex, string name, string stage)
    {
        IntPtr code = SlangNative.spGetEntryPointCode(request, entryPointIndex, out nuint size);
        if (code == IntPtr.Zero || size == 0)
        {
            throw new ShaderValidationException($"Slang produced no {stage} SPIR-V for '{name}'.");
        }

        byte[] bytes = new byte[size];
        Marshal.Copy(code, bytes, 0, (int)size);
        // SPIR-V magic word (little-endian layout in the byte stream).
        if (bytes.Length < 4 || bytes[0] != 0x03 || bytes[1] != 0x02 || bytes[2] != 0x23 || bytes[3] != 0x07)
        {
            throw new ShaderValidationException($"Slang produced invalid {stage} SPIR-V for '{name}'.");
        }
        bytes = StripRedundantDrawParametersCapability(bytes);
        bytes = SlangBaseInstanceZeroer.ZeroBaseInstance(bytes);
        return bytes;
    }

    /// <summary>
    /// Drop the redundant <c>OpCapability DrawParameters</c> (with its companion
    /// extension) from a SPIR-V module. Slang declares it for SV_InstanceID even
    /// when targeting Vulkan 1.1+ - where InstanceIndex is core and the capability
    /// is implicit - and wgpu's shader validator rejects the declaration outright.
    /// </summary>
    private static byte[] StripRedundantDrawParametersCapability(byte[] spirv)
    {
        const ushort opCapability = 17;
        const ushort opExtension = 10;
        const uint capabilityDrawParameters = 4427;

        int write = 20; // Skip the 5-word header.
        int read = 20;
        while (read < spirv.Length)
        {
            uint word = BitConverter.ToUInt32(spirv, read);
            int wordCount = (int)(word >> 16);
            int byteCount = wordCount * 4;
            if (wordCount == 0 || read + byteCount > spirv.Length)
            {
                return spirv;
            }

            ushort opcode = (ushort)(word & 0xFFFF);
            bool drop = opcode == opCapability && wordCount == 2 &&
                BitConverter.ToUInt32(spirv, read + 4) == capabilityDrawParameters;
            if (!drop && opcode == opExtension && wordCount >= 2)
            {
                // The literal is (wordCount - 1) words, null-terminated.
                string extension = System.Text.Encoding.ASCII
                    .GetString(spirv, read + 4, (wordCount - 1) * 4).TrimEnd('\0');
                if (extension.Contains("shader_draw_parameters"))
                {
                    drop = true;
                }
            }

            if (!drop)
            {
                if (write != read)
                {
                    Buffer.BlockCopy(spirv, read, spirv, write, byteCount);
                }
                write += byteCount;
            }
            read += byteCount;
        }

        if (write == spirv.Length)
        {
            return spirv;
        }
        byte[] stripped = new byte[write];
        Buffer.BlockCopy(spirv, 0, stripped, 0, write);
        return stripped;
    }
}
