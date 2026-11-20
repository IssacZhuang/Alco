// Immutable NGS shader reflection snapshots: root arguments, types, entry points, attributes, and generic parameters.

using System.Runtime.CompilerServices;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>The reflected category of a shader type.</summary>
public enum ShaderTypeKind : byte
{
    None = 0,
    Struct = 1,
    Array = 2,
    Matrix = 3,
    Vector = 4,
    Scalar = 5,
    ConstantBuffer = 6,
    Resource = 7,
    SamplerState = 8,
    TextureBuffer = 9,
    ShaderStorageBuffer = 10,
    ParameterBlock = 11,
    GenericParameter = 12,
    Interface = 13,
    OutputStream = 14,
    MeshOutput = 15,
    Specialized = 16,
    Feedback = 17,
    Pointer = 18,
    DynamicResource = 19,
    Enum = 20,
}

/// <summary>The scalar component type of a scalar, vector, matrix, or enum layout.</summary>
public enum ShaderScalarType : byte
{
    None = 0,
    Void = 1,
    Bool = 2,
    Int32 = 3,
    UInt32 = 4,
    Int64 = 5,
    UInt64 = 6,
    Float16 = 7,
    Float32 = 8,
    Float64 = 9,
    Int8 = 10,
    UInt8 = 11,
    Int16 = 12,
    UInt16 = 13,
}

/// <summary>Memory layout order of a reflected matrix type.</summary>
public enum ShaderMatrixLayout : byte
{
    None = 0,
    RowMajor = 1,
    ColumnMajor = 2,
}

/// <summary>NGS bindless resource semantic inferred from a reflected field type.</summary>
public enum ShaderFieldSemantic : byte
{
    /// <summary>Ordinary uniform or structured data.</summary>
    Data = 0,

    /// <summary>An eight-byte <c>GPUBufferView</c> buffer handle.</summary>
    BufferView = 1,

    /// <summary>A four-byte <c>GpuTextureId</c> descriptor index.</summary>
    TextureId = 2,

    /// <summary>A four-byte <c>GpuSamplerId</c> descriptor index.</summary>
    SamplerId = 3,
}

/// <summary>One argument inside a reflected shader user attribute.</summary>
public sealed class ShaderAttributeArgument
{
    /// <summary>The scalar kind of the argument, or <see cref="ShaderScalarType.None"/> for string arguments.</summary>
    public required ShaderScalarType ScalarType { get; init; }

    /// <summary>The string argument text when the argument is a string; otherwise null.</summary>
    public string? StringValue { get; init; }

    /// <summary>The integer argument value when integral or boolean.</summary>
    public long IntValue { get; init; }

    /// <summary>The floating-point argument value when floating point.</summary>
    public float FloatValue { get; init; }

    /// <summary>A human-readable string representation of the argument value.</summary>
    public required string DisplayValue { get; init; }
}

/// <summary>One user-defined attribute attached to a shader type, field, or entry point.</summary>
public sealed class ShaderAttribute
{
    /// <summary>The attribute identifier.</summary>
    public required string Name { get; init; }

    /// <summary>The attribute arguments in declaration order.</summary>
    public required IReadOnlyList<ShaderAttributeArgument> Arguments { get; init; }
}

/// <summary>One struct field with its byte offset, size, NGS semantic, and type layout.</summary>
public sealed class ShaderFieldLayout
{
    /// <summary>The declared field name (or dotted path for resolved nested lookups).</summary>
    public required string Name { get; init; }

    /// <summary>The byte offset within the enclosing struct or root.</summary>
    public required uint OffsetBytes { get; init; }

    /// <summary>The field size in bytes.</summary>
    public required uint SizeBytes { get; init; }

    /// <summary>The NGS bindless resource semantic of this field.</summary>
    public required ShaderFieldSemantic Semantic { get; init; }

    /// <summary>The field's type layout.</summary>
    public required ShaderTypeLayout Type { get; init; }

    /// <summary>User attributes declared on the field.</summary>
    public IReadOnlyList<ShaderAttribute> Attributes { get; init; } = [];
}

/// <summary>Immutable C-layout description of a reflected shader type.</summary>
public sealed class ShaderTypeLayout
{
    /// <summary>The reflected type category.</summary>
    public required ShaderTypeKind Kind { get; init; }

    /// <summary>The declared name of a named type, or null for built-in types.</summary>
    public string? TypeName { get; init; }

    /// <summary>The size in bytes under NGS C/structured layout, or zero when unsized.</summary>
    public required uint SizeBytes { get; init; }

    /// <summary>The required byte alignment of this type.</summary>
    public uint AlignmentBytes { get; init; }

    /// <summary>The element stride in bytes for arrays, or zero otherwise.</summary>
    public uint StrideBytes { get; init; }

    /// <summary>The element count for fixed-size arrays, or zero otherwise.</summary>
    public uint ElementCount { get; init; }

    /// <summary>The element or pointee type layout for arrays, wrappers, and pointers.</summary>
    public ShaderTypeLayout? ElementType { get; init; }

    /// <summary>The underlying scalar type for scalars, vectors, matrices, and enums.</summary>
    public ShaderScalarType ScalarType { get; init; }

    /// <summary>The component count of a vector type, or zero otherwise.</summary>
    public byte VectorLength { get; init; }

    /// <summary>The row count of a matrix type, or zero otherwise.</summary>
    public byte MatrixRows { get; init; }

    /// <summary>The column count of a matrix type, or zero otherwise.</summary>
    public byte MatrixColumns { get; init; }

    /// <summary>The storage layout of a matrix type.</summary>
    public ShaderMatrixLayout MatrixLayout { get; init; }

    /// <summary>The inferred NGS resource semantic of this type.</summary>
    public ShaderFieldSemantic Semantic { get; init; }

    /// <summary>Fields of a struct type in declaration order.</summary>
    public IReadOnlyList<ShaderFieldLayout> Fields { get; init; } = [];

    /// <summary>User attributes declared on this type.</summary>
    public IReadOnlyList<ShaderAttribute> Attributes { get; init; } = [];

    /// <summary>Finds a direct or dotted nested field within this struct type.</summary>
    /// <param name="nameOrPath">The field name or dot-separated path.</param>
    /// <returns>The resolved field with cumulative byte offset, or null when absent.</returns>
    public ShaderFieldLayout? FindField(string nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath) || Fields.Count == 0)
            return null;
        for (int i = 0; i < Fields.Count; i++)
        {
            if (Fields[i].Name == nameOrPath)
                return Fields[i];
        }
        int dot = nameOrPath.IndexOf('.');
        if (dot <= 0)
            return null;
        return FindNestedField(Fields, nameOrPath);
    }

    internal static ShaderFieldLayout? FindNestedField(IReadOnlyList<ShaderFieldLayout> rootFields, string path)
    {
        ReadOnlySpan<char> remaining = path.AsSpan();
        IReadOnlyList<ShaderFieldLayout> currentFields = rootFields;
        uint baseOffset = 0;
        while (!remaining.IsEmpty)
        {
            int dot = remaining.IndexOf('.');
            ReadOnlySpan<char> segment = dot < 0 ? remaining : remaining[..dot];
            ShaderFieldLayout? matched = null;
            for (int i = 0; i < currentFields.Count; i++)
            {
                if (segment.SequenceEqual(currentFields[i].Name.AsSpan()))
                {
                    matched = currentFields[i];
                    break;
                }
            }
            if (matched == null)
                return null;
            baseOffset += matched.OffsetBytes;
            if (dot < 0)
            {
                return baseOffset == matched.OffsetBytes
                    ? matched
                    : new ShaderFieldLayout
                    {
                        Name = path,
                        OffsetBytes = baseOffset,
                        SizeBytes = matched.SizeBytes,
                        Semantic = matched.Semantic,
                        Type = matched.Type,
                        Attributes = matched.Attributes,
                    };
            }
            currentFields = matched.Type.Fields;
            if (currentFields.Count == 0)
                return null;
            remaining = remaining[(dot + 1)..];
        }
        return null;
    }
}

/// <summary>Reflected layout of a shader's <c>GPU_ROOT</c> argument block.</summary>
public sealed class ShaderRootLayout
{
    /// <summary>The declared root struct name.</summary>
    public required string TypeName { get; init; }

    /// <summary>The root struct size in bytes.</summary>
    public required uint SizeBytes { get; init; }

    /// <summary>The root struct alignment in bytes.</summary>
    public required uint AlignmentBytes { get; init; }

    /// <summary>Top-level root fields in declaration order.</summary>
    public required IReadOnlyList<ShaderFieldLayout> Fields { get; init; }

    /// <summary>Finds a field by name or dotted nested path, accumulating byte offsets.</summary>
    /// <param name="nameOrPath">The field name or dot-separated nested path.</param>
    /// <returns>The field layout, or null when no matching field exists.</returns>
    public ShaderFieldLayout? Find(string nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath))
            return null;
        for (int i = 0; i < Fields.Count; i++)
        {
            if (Fields[i].Name == nameOrPath)
                return Fields[i];
        }
        return nameOrPath.Contains('.') ? ShaderTypeLayout.FindNestedField(Fields, nameOrPath) : null;
    }

    /// <summary>Checks whether unmanaged C# type <typeparamref name="T"/> matches the reflected root byte size.</summary>
    /// <typeparam name="T">The CPU mirror struct type.</typeparam>
    /// <returns>True when <c>sizeof(T)</c> equals <see cref="SizeBytes"/>.</returns>
    public bool MatchesSize<T>() where T : unmanaged => (uint)Unsafe.SizeOf<T>() == SizeBytes;
}

/// <summary>One stage input or output parameter reflected from an entry point.</summary>
public sealed class ShaderStageVariable
{
    /// <summary>The declared parameter or output name.</summary>
    public required string Name { get; init; }

    /// <summary>The HLSL/Slang semantic name (for example <c>SV_Position</c> or <c>TEXCOORD</c>), or null.</summary>
    public string? SemanticName { get; init; }

    /// <summary>The semantic index.</summary>
    public uint SemanticIndex { get; init; }

    /// <summary>The variable's type layout.</summary>
    public required ShaderTypeLayout Type { get; init; }
}

/// <summary>One generic type or value specialization parameter declared on an entry point.</summary>
public sealed class ShaderGenericParam
{
    /// <summary>The parameter identifier.</summary>
    public required string Name { get; init; }

    /// <summary>True for a generic type parameter; false for a compile-time constant value parameter.</summary>
    public required bool IsTypeParameter { get; init; }

    /// <summary>The interface or constraint type name for type parameters, or null.</summary>
    public string? ConstraintTypeName { get; init; }

    /// <summary>The scalar type for value parameters, or <see cref="ShaderScalarType.None"/>.</summary>
    public ShaderScalarType ValueScalarType { get; init; }
}

/// <summary>Reflected metadata for one compiled entry point.</summary>
public sealed class ShaderEntryPointReflection
{
    /// <summary>The entry point function name in shader source.</summary>
    public required string SourceName { get; init; }

    /// <summary>The entry point name embedded in the compiled target code.</summary>
    public required string CompiledEntryPoint { get; init; }

    /// <summary>The programmable pipeline stage.</summary>
    public required ShaderStageKind Stage { get; init; }

    /// <summary>Resolved threadgroup dimensions for compute, mesh, and task stages.</summary>
    public required Extent3D ThreadgroupSize { get; init; }

    /// <summary>User attributes attached to the entry point function.</summary>
    public IReadOnlyList<ShaderAttribute> Attributes { get; init; } = [];

    /// <summary>Stage input parameters in declaration order.</summary>
    public IReadOnlyList<ShaderStageVariable> Inputs { get; init; } = [];

    /// <summary>Stage output values in declaration order.</summary>
    public IReadOnlyList<ShaderStageVariable> Outputs { get; init; } = [];

    /// <summary>Generic type and value specialization parameters declared on the entry point.</summary>
    public IReadOnlyList<ShaderGenericParam> GenericParameters { get; init; } = [];
}

/// <summary>One global parameter reflected from a linked shader program.</summary>
public sealed class ShaderParameterInfo
{
    /// <summary>The global parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>The assigned binding slot index.</summary>
    public required uint BindingIndex { get; init; }

    /// <summary>The assigned descriptor set or register space.</summary>
    public required uint BindingSpace { get; init; }

    /// <summary>The parameter's type layout.</summary>
    public required ShaderTypeLayout Type { get; init; }
}

/// <summary>Summary metadata for one entry point discovered in an uncompiled <see cref="ShaderModule"/>.</summary>
/// <param name="Name">The entry point function name.</param>
/// <param name="Stage">The entry point shader stage.</param>
/// <param name="HasGenericParameters">Whether the entry point declares generic specialization parameters.</param>
public readonly record struct ShaderModuleEntryPoint(string Name, ShaderStageKind Stage, bool HasGenericParameters);

/// <summary>Pure-managed, lifetime-independent reflection snapshot of a compiled NGS shader program.</summary>
public sealed class ShaderReflection
{
    /// <summary>
    /// The concrete <c>GPU_ROOT</c> or generic <c>[GPURoot]</c> layout, if present. Generic root parameters bind to entry-point
    /// parameters by name and kind, or in order when the remaining parameters of that kind have equal counts.
    /// </summary>
    public ShaderRootLayout? Root { get; init; }

    /// <summary>Reflected entry points in compiled order.</summary>
    public required IReadOnlyList<ShaderEntryPointReflection> EntryPoints { get; init; }

    /// <summary>Named struct and enum types declared in the compiled module.</summary>
    public required IReadOnlyList<ShaderTypeLayout> Types { get; init; }

    /// <summary>Global parameters of the linked program.</summary>
    public required IReadOnlyList<ShaderParameterInfo> Globals { get; init; }

    /// <summary>Finds a reflected struct or enum type layout by name.</summary>
    /// <param name="typeName">The type name or <c>GPURootArguments</c> alias.</param>
    /// <returns>The type layout, or null when not present.</returns>
    public ShaderTypeLayout? FindType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;
        if (typeName == "GPURootArguments" && Root != null)
        {
            for (int i = 0; i < Types.Count; i++)
            {
                if (Types[i].TypeName == Root.TypeName)
                    return Types[i];
            }
            return new ShaderTypeLayout
            {
                Kind = ShaderTypeKind.Struct,
                TypeName = Root.TypeName,
                SizeBytes = Root.SizeBytes,
                AlignmentBytes = Root.AlignmentBytes,
                Fields = Root.Fields,
            };
        }
        for (int i = 0; i < Types.Count; i++)
        {
            if (Types[i].TypeName == typeName)
                return Types[i];
        }
        return null;
    }

    /// <summary>Finds a reflected entry point by its source function name.</summary>
    /// <param name="sourceName">The entry point function name in shader source.</param>
    /// <returns>The reflected entry point, or null when absent.</returns>
    public ShaderEntryPointReflection? FindEntryPoint(string sourceName)
    {
        for (int i = 0; i < EntryPoints.Count; i++)
        {
            if (EntryPoints[i].SourceName == sourceName)
                return EntryPoints[i];
        }
        return null;
    }

    /// <summary>Checks whether unmanaged C# type <typeparamref name="T"/> matches the reflected byte size of <paramref name="typeName"/>.</summary>
    /// <typeparam name="T">The CPU struct type.</typeparam>
    /// <param name="typeName">The shader struct or <c>GPURootArguments</c> name.</param>
    /// <returns>True when the type exists and <c>sizeof(T)</c> equals its byte size.</returns>
    public bool MatchesStructLayout<T>(string typeName) where T : unmanaged
    {
        ShaderTypeLayout? layout = FindType(typeName);
        return layout != null && (uint)Unsafe.SizeOf<T>() == layout.SizeBytes;
    }
}
