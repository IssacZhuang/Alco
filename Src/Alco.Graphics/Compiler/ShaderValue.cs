using System.Numerics;

namespace Alco.Graphics;

/// <summary>
/// One authored material-parameter value — the flexible authoring currency of the
/// <c>[MaterialParams]</c> table. Holds either a float component tuple (scalar
/// through float4, plus <see langword="bool"/> and <see cref="int"/>/<see cref="uint"/>
/// for typed members), a <see cref="Matrix4x4"/>, or an array of same-kind elements.
/// JSON accepts a number, a hex color string, or a per-component object,
/// plus the natural forms of the typed members (a JSON integer for
/// <see langword="int"/>/<see langword="uint"/>, <c>true</c>/<c>false</c> for
/// <see langword="bool"/>, an array for array members); code constructs values
/// through the implicit conversions.
/// </summary>
public readonly struct ShaderValue : IEquatable<ShaderValue>
{
    private readonly float[] _floats;
    private readonly int[] _ints;
    private readonly Matrix4x4 _matrix;

    /// <summary>The kind of the value, selecting the storage read by the marshaler.</summary>
    public ShaderValueKind Kind { get; }

    /// <summary>The component count of one element (1-4 for tuples, 16 for a matrix).</summary>
    public int ComponentCount { get; }

    /// <summary>The element count of an array value (1 for plain values).</summary>
    public int ElementCount { get; }

    private ShaderValue(ShaderValueKind kind, int componentCount, int elementCount, float[] floats, int[]? ints, Matrix4x4 matrix)
    {
        Kind = kind;
        ComponentCount = componentCount;
        ElementCount = elementCount;
        _floats = floats;
        _ints = ints ?? [];
        _matrix = matrix;
    }

    /// <summary>A float scalar (a JSON number with a fraction reads here; so does a broadcast legacy value).</summary>
    public static implicit operator ShaderValue(float value) => Floats([value], 1);

    /// <summary>A float2 tuple.</summary>
    public static implicit operator ShaderValue(Vector2 value) => Floats([value.X, value.Y], 2);

    /// <summary>A float3 tuple (colors author through this).</summary>
    public static implicit operator ShaderValue(Vector3 value) => Floats([value.X, value.Y, value.Z], 3);

    /// <summary>A float4 tuple (the legacy Vector4 table values).</summary>
    public static implicit operator ShaderValue(Vector4 value) => Floats([value.X, value.Y, value.Z, value.W], 4);

    /// <summary>An <see langword="int"/> member value (a JSON integer without a fraction).</summary>
    public static implicit operator ShaderValue(int value) => new(ShaderValueKind.Int32, 1, 1, [], [value], Matrix4x4.Identity);

    /// <summary>A <see langword="uint"/> member value.</summary>
    public static implicit operator ShaderValue(uint value) => new(ShaderValueKind.UInt32, 1, 1, [], [(int)value], Matrix4x4.Identity);

    /// <summary>A <see langword="bool"/> member value (marshals to the GPU's 4-byte 0/1).</summary>
    public static implicit operator ShaderValue(bool value) => new(ShaderValueKind.Bool32, 1, 1, [], [value ? 1 : 0], Matrix4x4.Identity);

    /// <summary>A float array member value (one scalar per element).</summary>
    public static ShaderValue Floats(float[] elements) =>
        new(ShaderValueKind.Float32, 1, elements.Length, elements, [], Matrix4x4.Identity);

    /// <summary>An int array member value.</summary>
    public static ShaderValue Ints(int[] elements) => new(ShaderValueKind.Int32, 1, elements.Length, [], elements, Matrix4x4.Identity);

    /// <summary>A bool array member value.</summary>
    public static ShaderValue Bools(bool[] elements)
        => new(ShaderValueKind.Bool32, 1, elements.Length, [], Array.ConvertAll(elements, b => b ? 1 : 0), Matrix4x4.Identity);

    /// <summary>A matrix member value.</summary>
    public static ShaderValue Matrix(Matrix4x4 value) => new(ShaderValueKind.Float32, 16, 1,
        [value.M11, value.M12, value.M13, value.M14,
         value.M21, value.M22, value.M23, value.M24,
         value.M31, value.M32, value.M33, value.M34,
         value.M41, value.M42, value.M43, value.M44], [], value);

    /// <summary>A float tuple of an explicit component count (2-4; JSON component objects).</summary>
    public static ShaderValue Floats(float[] components, int componentCount) =>
        new(ShaderValueKind.Float32, componentCount, 1, components, [], Matrix4x4.Identity);

    /// <summary>The float components of one element (float kind).</summary>
    /// <param name="element">The element index (plain values take 0).</param>
    public ReadOnlySpan<float> GetFloats(int element = 0)
        => _floats.AsSpan(element * ComponentCount, ComponentCount);

    /// <summary>The whole float storage as one flat scalar list (float kind) —
    /// the marshaling view that chops into array-member elements.</summary>
    public ReadOnlySpan<float> AsFloatList() => _floats;

    /// <summary>The integer image of one element (int/uint/bool kinds).</summary>
    /// <param name="element">The element index (plain values take 0).</param>
    public int GetInt(int element = 0) => _ints[element];

    /// <summary>The matrix (matrix kind).</summary>
    public Matrix4x4 GetMatrix() => _matrix;

    /// <inheritdoc />
    public bool Equals(ShaderValue other)
    {
        return Kind == other.Kind
            && ComponentCount == other.ComponentCount
            && ElementCount == other.ElementCount
            && _ints.AsSpan().SequenceEqual(other._ints)
            && _floats.AsSpan().SequenceEqual(other._floats)
            && _matrix.Equals(other._matrix);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ShaderValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, ComponentCount, ElementCount);

    /// <inheritdoc />
    public override string ToString()
    {
        string kind = Kind switch
        {
            ShaderValueKind.Float32 => ComponentCount == 16 ? "matrix" : $"float{ComponentCount}",
            ShaderValueKind.Int32 => "int",
            ShaderValueKind.UInt32 => "uint",
            ShaderValueKind.Bool32 => "bool",
            _ => Kind.ToString(),
        };
        return ElementCount > 1 ? $"{kind}[{ElementCount}]" : kind;
    }
}

/// <summary>The scalar kind of one <see cref="ShaderValue"/>.</summary>
public enum ShaderValueKind
{
    /// <summary>Float components (a tuple or a matrix).</summary>
    Float32 = ShaderUniformScalarType.Float32,
    /// <summary>An <see langword="int"/> value.</summary>
    Int32 = ShaderUniformScalarType.Int32,
    /// <summary>A <see langword="uint"/> value.</summary>
    UInt32 = ShaderUniformScalarType.UInt32,
    /// <summary>A <see langword="bool"/> value.</summary>
    Bool32 = ShaderUniformScalarType.Bool32,
}
