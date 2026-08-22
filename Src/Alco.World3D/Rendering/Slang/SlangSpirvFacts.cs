using Alco.Graphics;
using Alco.Graphics.Spirv;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Facts Slang's own reflection API does not expose for engine pipeline shaders,
/// read directly from the compiled SPIR-V instead (the engine's DXC path reads
/// the same facts with its internal reflector):
/// <list type="bullet">
/// <item>the compute thread group size (declared by <c>[numthreads]</c>, emitted
/// as <c>OpExecutionMode LocalSize</c>);</item>
/// <item>the pixel format of a storage image (declared by
/// <c>[[vk::image_format(...)]]</c>, the operand of <c>OpTypeImage</c>) - wgpu
/// requires the bind group layout's storage texture format to match the
/// shader's declared format exactly.</item>
/// </list>
/// </summary>
internal static class SlangSpirvFacts
{
    /// <summary>The thread group size of a compute module (default 1x1x1 when absent).</summary>
    public static ThreadGroupSize ReadThreadGroupSize(byte[] spirv)
    {
        SpirvModule module = Parse(spirv);
        if (module.EntryPoints.Count == 0 ||
            (SpirvExecutionModel)module.EntryPoints[0][1] != SpirvExecutionModel.GLCompute)
        {
            return ThreadGroupSize.Default;
        }

        uint entryPointId = module.EntryPoints[0][2];
        foreach (SpirvInstruction mode in module.ExecutionModes)
        {
            if (mode[1] == entryPointId && (SpirvExecutionMode)mode[2] == SpirvExecutionMode.LocalSize)
            {
                return new ThreadGroupSize(mode[3], mode[4], mode[5]);
            }
        }

        return ThreadGroupSize.Default;
    }

    /// <summary>
    /// Try to read the storage image format of the named global variable: the
    /// image-format operand of the <c>OpTypeImage</c> behind the variable's
    /// pointer type, mapped to the engine pixel format. Returns false for
    /// unknown names (another stage's module may declare the variable) or
    /// formats outside the engine's storage-texture set.
    /// </summary>
    public static bool TryReadStorageImageFormat(byte[] spirv, string variableName, out PixelFormat format)
    {
        SpirvModule module = Parse(spirv);

        uint variableId = 0;
        foreach (KeyValuePair<uint, string> pair in module.Names)
        {
            if (pair.Value == variableName)
            {
                variableId = pair.Key;
                break;
            }
        }
        if (variableId == 0)
        {
            format = PixelFormat.Undefined;
            return false;
        }

        Dictionary<uint, int> variables = [];
        Dictionary<uint, int> pointers = [];
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            SpirvInstruction inst = module.Instructions[i];
            if ((SpirvOp)inst.OpCode == SpirvOp.Variable)
            {
                variables[inst[2]] = i;
            }
            else if ((SpirvOp)inst.OpCode == SpirvOp.TypePointer)
            {
                pointers[inst[1]] = i;
            }
        }

        if (!variables.TryGetValue(variableId, out int variableIndex))
        {
            format = PixelFormat.Undefined;
            return false;
        }

        // OpVariable: [pointerType, resultId, storageClass, ...]
        uint pointerTypeId = module.Instructions[variableIndex][1];
        if (!pointers.TryGetValue(pointerTypeId, out int pointerIndex))
        {
            format = PixelFormat.Undefined;
            return false;
        }

        // OpTypePointer: [resultId, storageClass, pointeeTypeId]
        uint imageTypeId = module.Instructions[pointerIndex][3];
        SpirvInstruction? imageType = module.GetInstruction(imageTypeId);
        if (imageType == null || (SpirvOp)imageType.OpCode != SpirvOp.TypeImage)
        {
            format = PixelFormat.Undefined;
            return false;
        }

        // OpTypeImage: [resultId, sampledType, dim, depth, arrayed, ms, sampled, format]
        format = ConvertImageFormat((SpirvImageFormat)imageType[8]);
        return format != PixelFormat.Undefined;
    }

    private static SpirvModule Parse(byte[] spirv)
    {
        try
        {
            return SpirvReader.Parse(spirv);
        }
        catch (Exception ex) when (ex is ShaderReflectionException or ShaderCompilationException)
        {
            throw new ShaderValidationException($"Slang produced a SPIR-V module that cannot be parsed: {ex.Message}");
        }
    }

    /// <summary>
    /// The storage-image formats the engine's shaders declare via
    /// <c>DEFINE_TEX2D/3D_STORAGE</c>; anything else is rejected so an
    /// unsupported combination surfaces at compile time, not as a wgpu
    /// validation error.
    /// </summary>
    private static PixelFormat ConvertImageFormat(SpirvImageFormat format)
    {
        return format switch
        {
            SpirvImageFormat.Rgba8 => PixelFormat.RGBA8Unorm,
            SpirvImageFormat.Rgba8Snorm => PixelFormat.RGBA8Snorm,
            SpirvImageFormat.Rgba16f => PixelFormat.RGBA16Float,
            SpirvImageFormat.Rgba32f => PixelFormat.RGBA32Float,
            SpirvImageFormat.R32f => PixelFormat.R32Float,
            SpirvImageFormat.Rgba32ui => PixelFormat.RGBA32Uint,
            _ => PixelFormat.Undefined,
        };
    }
}
