using System.Runtime.InteropServices;
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
/// Compiles the Slang material wrappers of World3D to SPIR-V and reflects them
/// with Slang's own reflection API. One compile contains the vertex and pixel
/// entry points of a (pass template, surface) pair, so both stages share a
/// single program layout. The engine's DXC + SPIR-V-reflect toolchain is not
/// involved. A Slang session is not thread-safe; all compiles serialize on a
/// lock, mirroring how the engine serializes DXC compiles.
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

                int translationUnit = SlangNative.spAddTranslationUnit(
                    request, SlangNative.SLANG_SOURCE_LANGUAGE_SLANG, name);
                SlangNative.spAddTranslationUnitSourceString(
                    request, translationUnit, $"{name}.slang", source);
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
