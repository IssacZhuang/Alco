namespace Alco.Graphics.Spirv;

/// <summary>
/// SPIR-V opcodes used by the engine's parser, reflector, and patcher.
/// Values are stable across SPIR-V versions (spirv.core.grammar.json).
/// </summary>
public enum SpirvOp : ushort
{
    Name = 5,
    MemberName = 6,
    Decorate = 71,
    MemberDecorate = 72,
    TypeVoid = 19,
    TypeBool = 20,
    TypeInt = 21,
    TypeFloat = 22,
    TypeVector = 23,
    TypeMatrix = 24,
    TypeImage = 25,
    TypeSampler = 26,
    TypeSampledImage = 27,
    TypeArray = 28,
    TypeRuntimeArray = 29,
    TypeStruct = 30,
    TypePointer = 32,
    TypeFunction = 33,
    Constant = 43,
    Function = 54,
    FunctionEnd = 56,
    Variable = 59,
    Load = 61,
    AccessChain = 65,
    SampledImage = 86,
    Image = 100,
    EntryPoint = 15,
    ExecutionMode = 16,
    MemoryModel = 14,
}

/// <summary>
/// SPIR-V decoration values (Decoration enum from the specification).
/// </summary>
public enum SpirvDecoration : uint
{
    Block = 2,
    BufferBlock = 3,
    BuiltIn = 11,
    NonReadable = 15,
    NonWritable = 24,
    Location = 30,
    Binding = 33,
    DescriptorSet = 34,
    Offset = 35,
    ArrayStride = 6,
    MatrixStride = 7,
}

/// <summary>
/// SPIR-V storage classes relevant to the engine.
/// </summary>
public enum SpirvStorageClass : uint
{
    UniformConstant = 0,
    Input = 1,
    Uniform = 2,
    Output = 3,
    PushConstant = 9,
    StorageBuffer = 12,
}

/// <summary>
/// SPIR-V execution models.
/// </summary>
public enum SpirvExecutionModel : uint
{
    Vertex = 0,
    Fragment = 4,
    GLCompute = 5,
    Geometry = 3,
    TessellationControl = 2,
    TessellationEvaluation = 1,
}

/// <summary>
/// SPIR-V execution modes relevant to the engine.
/// </summary>
public enum SpirvExecutionMode : uint
{
    LocalSize = 17,
}

/// <summary>
/// SPIR-V dimensionality for OpTypeImage.
/// </summary>
public enum SpirvDim : uint
{
    Dim1D = 0,
    Dim2D = 1,
    Dim3D = 2,
    DimCube = 3,
}

/// <summary>
/// SPIR-V image formats (ImageFormat operand of OpTypeImage).
/// Values from the SPIR-V specification (spirv.h).
/// </summary>
public enum SpirvImageFormat : uint
{
    Unknown = 0,
    Rgba32f = 1,
    Rgba16f = 2,
    R32f = 3,
    Rgba8 = 4,
    Rgba8Snorm = 5,
    Rg32f = 6,
    Rg16f = 7,
    R11fG11fB10f = 8,
    R16f = 9,
    Rgba16 = 10,
    Rgb10A2 = 11,
    Rg16 = 12,
    Rg8 = 13,
    R16 = 14,
    R8 = 15,
    Rgba16Snorm = 16,
    Rg16Snorm = 17,
    Rg8Snorm = 18,
    R16Snorm = 19,
    R8Snorm = 20,
    Rgba32i = 21,
    Rgba16i = 22,
    Rgba8i = 23,
    R32i = 24,
    Rg32i = 25,
    Rg16i = 26,
    Rg8i = 27,
    R16i = 28,
    R8i = 29,
    Rgba32ui = 30,
    Rgba16ui = 31,
    Rgba8ui = 32,
    R32ui = 33,
    Rgb10a2ui = 34,
    Rg32ui = 35,
    Rg16ui = 36,
    Rg8ui = 37,
    R16ui = 38,
    R8ui = 39,
}
