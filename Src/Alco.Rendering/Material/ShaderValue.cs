using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Scalar component representation stored inside a <see cref="ShaderValue"/>.</summary>
public enum ShaderValueKind : byte
{
    /// <summary>32-bit boolean value (0 or 1).</summary>
    Bool32 = 0,

    /// <summary>32-bit signed integer value.</summary>
    Int32 = 1,

    /// <summary>32-bit unsigned integer value.</summary>
    UInt32 = 2,

    /// <summary>32-bit IEEE 754 floating-point value.</summary>
    Float32 = 3,
}

/// <summary>
/// Immutable authored shader parameter or specialization value converted against
/// <see cref="ShaderReflection"/> layouts on upload to a <see cref="Material"/>.
/// </summary>
public readonly struct ShaderValue
{
    private readonly float[]? _floats;
    private readonly int[]? _ints;
    private readonly bool _isCompound;

    private ShaderValue(ShaderValueKind kind, int componentCount, int elementCount, float[]? floats, int[]? ints, bool isCompound)
    {
        Kind = kind;
        ComponentCount = componentCount;
        ElementCount = elementCount;
        _floats = floats;
        _ints = ints;
        _isCompound = isCompound;
    }

    /// <summary>Gets the scalar value category.</summary>
    public ShaderValueKind Kind { get; }

    /// <summary>Gets the component count per element (1..16).</summary>
    public int ComponentCount { get; }

    /// <summary>Gets the number of elements stored in this value.</summary>
    public int ElementCount { get; }

    /// <summary>Creates a boolean scalar value.</summary>
    public static ShaderValue Bool(bool value) =>
        new(ShaderValueKind.Bool32, 1, 1, null, [value ? 1 : 0], isCompound: false);

    /// <summary>Creates a signed integer scalar value.</summary>
    public static ShaderValue Int(int value) =>
        new(ShaderValueKind.Int32, 1, 1, null, [value], isCompound: false);

    /// <summary>Creates an unsigned integer scalar value.</summary>
    public static ShaderValue UInt(uint value) =>
        new(ShaderValueKind.UInt32, 1, 1, null, [unchecked((int)value)], isCompound: false);

    /// <summary>Creates a single-precision floating-point scalar value.</summary>
    public static ShaderValue Float(float value) =>
        new(ShaderValueKind.Float32, 1, 1, [value], null, isCompound: false);

    /// <summary>Creates a compound float vector or array value.</summary>
    public static ShaderValue Floats(ReadOnlySpan<float> values) =>
        new(ShaderValueKind.Float32, values.Length, values.Length > 0 ? 1 : 0, values.ToArray(), null, isCompound: true);

    /// <summary>Creates a float vector value taking the first <paramref name="componentCount"/> components.</summary>
    public static ShaderValue Floats(ReadOnlySpan<float> values, int componentCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(componentCount, 0);
        if (componentCount > values.Length)
            throw new ArgumentOutOfRangeException(nameof(componentCount));
        return new(ShaderValueKind.Float32, componentCount, 1, values[..componentCount].ToArray(), null, isCompound: true);
    }

    /// <summary>Creates a signed integer vector or array value.</summary>
    public static ShaderValue Ints(ReadOnlySpan<int> values) =>
        new(ShaderValueKind.Int32, values.Length, values.Length > 0 ? 1 : 0, null, values.ToArray(), isCompound: true);

    /// <summary>Creates an unsigned integer vector or array value.</summary>
    public static ShaderValue UInts(ReadOnlySpan<uint> values)
    {
        int[] raw = new int[values.Length];
        MemoryMarshal.Cast<uint, int>(values).CopyTo(raw);
        return new(ShaderValueKind.UInt32, values.Length, values.Length > 0 ? 1 : 0, null, raw, isCompound: true);
    }

    public static implicit operator ShaderValue(bool value) => Bool(value);
    public static implicit operator ShaderValue(int value) => Int(value);
    public static implicit operator ShaderValue(uint value) => UInt(value);
    public static implicit operator ShaderValue(float value) => Float(value);
    public static implicit operator ShaderValue(Vector2 value) =>
        new(ShaderValueKind.Float32, 2, 1, [value.X, value.Y], null, isCompound: true);
    public static implicit operator ShaderValue(Vector3 value) =>
        new(ShaderValueKind.Float32, 3, 1, [value.X, value.Y, value.Z], null, isCompound: true);
    public static implicit operator ShaderValue(Vector4 value) =>
        new(ShaderValueKind.Float32, 4, 1, [value.X, value.Y, value.Z, value.W], null, isCompound: true);
    public static implicit operator ShaderValue(ColorFloat value) =>
        new(ShaderValueKind.Float32, 4, 1, [value.R, value.G, value.B, value.A], null, isCompound: true);
    public static implicit operator ShaderValue(Matrix4x4 value) =>
        new(ShaderValueKind.Float32, 16, 1,
        [
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44,
        ], null, isCompound: true);

    /// <summary>Reads an integer component of a scalar or compound integer element.</summary>
    public int GetInt(int element = 0) =>
        _ints != null ? _ints[element] :
        _floats != null ? (int)_floats[element] : 0;

    /// <summary>Reads an unsigned integer component of a scalar or compound integer element.</summary>
    public uint GetUInt(int element = 0) =>
        _ints != null ? unchecked((uint)_ints[element]) :
        _floats != null ? (uint)_floats[element] : 0u;

    /// <summary>Reads a single-precision floating-point component.</summary>
    public float GetFloat(int element = 0) =>
        _floats != null ? _floats[element] :
        _ints != null ? (Kind == ShaderValueKind.UInt32 ? unchecked((uint)_ints[element]) : _ints[element]) : 0f;

    /// <summary>Reads the floating-point components for the specified element.</summary>
    public ReadOnlySpan<float> GetFloats(int element = 0)
    {
        if (_floats == null || ElementCount == 0)
            return [];
        return _floats.AsSpan(element * ComponentCount, ComponentCount);
    }

    /// <summary>Formats a scalar specialization argument as a Slang constant expression string.</summary>
    public string ToSpecializationString()
    {
        if (ElementCount != 1 || ComponentCount != 1)
            throw new InvalidOperationException("Only scalar values can be used as shader specialization arguments.");
        return Kind switch
        {
            ShaderValueKind.Bool32 => GetInt() != 0 ? "true" : "false",
            ShaderValueKind.Int32 => GetInt().ToString(CultureInfo.InvariantCulture),
            ShaderValueKind.UInt32 => GetUInt().ToString(CultureInfo.InvariantCulture),
            ShaderValueKind.Float32 => GetFloat().ToString("R", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported specialization value kind '{Kind}'."),
        };
    }

    /// <summary>Converts and writes this value into <paramref name="destination"/> matching <paramref name="targetType"/>.</summary>
    public bool TryWriteTo(ShaderTypeLayout targetType, Span<byte> destination) =>
        TryWriteTo(targetType, destination, out _);

    /// <summary>Converts and writes this value into <paramref name="destination"/> matching <paramref name="targetType"/>.</summary>
    public bool TryWriteTo(ShaderTypeLayout targetType, Span<byte> destination, out string? error)
    {
        if (destination.Length < (int)targetType.SizeBytes)
        {
            error = $"Destination span of {destination.Length} bytes is smaller than target type size {targetType.SizeBytes}.";
            return false;
        }

        destination[..(int)targetType.SizeBytes].Clear();
        switch (targetType.Kind)
        {
            case ShaderTypeKind.Scalar:
            case ShaderTypeKind.Enum:
                return TryWriteScalar(targetType.ScalarType, targetType.SizeBytes, destination, out error);

            case ShaderTypeKind.Vector:
                return TryWriteVector(targetType, destination, out error);

            case ShaderTypeKind.Matrix:
                return TryWriteMatrix(targetType, destination, out error);

            case ShaderTypeKind.Array:
                return TryWriteArray(targetType, destination, out error);

            default:
                error = $"Unsupported shader parameter target type '{targetType.Kind}'.";
                return false;
        }
    }

    private bool TryWriteScalar(ShaderScalarType scalarType, uint sizeBytes, Span<byte> destination, out string? error)
    {
        if (ElementCount != 1 || ComponentCount != 1)
        {
            error = $"Expected scalar value, got {ElementCount * ComponentCount} components.";
            return false;
        }

        error = null;
        switch (scalarType)
        {
            case ShaderScalarType.Bool:
                if (Kind is not (ShaderValueKind.Bool32 or ShaderValueKind.Int32 or ShaderValueKind.UInt32))
                {
                    error = $"Cannot convert {Kind} to bool.";
                    return false;
                }
                bool b = Kind == ShaderValueKind.UInt32 ? GetUInt() != 0 : GetInt() != 0;
                if (sizeBytes == 1)
                    destination[0] = b ? (byte)1 : (byte)0;
                else
                    MemoryMarshal.Write(destination, b ? 1u : 0u);
                return true;

            case ShaderScalarType.Int8:
            case ShaderScalarType.Int16:
            case ShaderScalarType.Int32:
            case ShaderScalarType.Int64:
                if (!TryReadSignedScalar(0, out long signedVal, out error))
                    return false;
                return WriteSignedScalar(scalarType, signedVal, destination, out error);

            case ShaderScalarType.UInt8:
            case ShaderScalarType.UInt16:
            case ShaderScalarType.UInt32:
            case ShaderScalarType.UInt64:
                if (!TryReadUnsignedScalar(0, out ulong unsignedVal, out error))
                    return false;
                return WriteUnsignedScalar(scalarType, unsignedVal, destination, out error);

            case ShaderScalarType.Float16:
            case ShaderScalarType.Float32:
            case ShaderScalarType.Float64:
                if (!TryReadFloatComponent(0, out float floatVal, out error))
                    return false;
                if (scalarType == ShaderScalarType.Float16)
                    MemoryMarshal.Write(destination, (Half)floatVal);
                else if (scalarType == ShaderScalarType.Float32)
                    MemoryMarshal.Write(destination, floatVal);
                else
                    MemoryMarshal.Write(destination, (double)floatVal);
                return true;

            default:
                error = $"Unsupported scalar shader type '{scalarType}'.";
                return false;
        }
    }

    private bool TryWriteVector(ShaderTypeLayout targetType, Span<byte> destination, out string? error)
    {
        int totalSrc = ElementCount * ComponentCount;
        int len = targetType.VectorLength;
        if (!_isCompound && totalSrc == 1)
        {
            error = $"Cannot bind a scalar value to vector type '{targetType.ScalarType}[{len}]'.";
            return false;
        }
        if (totalSrc == 0 || (totalSrc > len && !(totalSrc == 4 && len == 3)))
        {
            error = $"Value has {totalSrc} components which cannot fit {targetType.ScalarType} vector of length {len}.";
            return false;
        }

        int copyCount = Math.Min(totalSrc, len);
        int elemSize = (int)(targetType.SizeBytes / len);
        error = null;

        for (int i = 0; i < copyCount; i++)
        {
            Span<byte> elemDst = destination.Slice(i * elemSize, elemSize);
            if (!TryWriteSingleComponentAt(targetType.ScalarType, (uint)elemSize, i, elemDst, out error))
                return false;
        }
        return true;
    }

    private bool TryWriteMatrix(ShaderTypeLayout targetType, Span<byte> destination, out string? error)
    {
        int totalSrc = ElementCount * ComponentCount;
        int rows = targetType.MatrixRows;
        int cols = targetType.MatrixColumns;
        int expected = rows * cols;
        if (targetType.ScalarType != ShaderScalarType.Float32 || totalSrc != expected || targetType.SizeBytes != (uint)(expected * 4))
        {
            error = $"Cannot write {totalSrc}-component {Kind} into {rows}x{cols} matrix ({targetType.ScalarType}, {targetType.SizeBytes}B).";
            return false;
        }
        if (Kind == ShaderValueKind.Float32 && _floats != null)
        {
            MemoryMarshal.AsBytes(_floats.AsSpan(0, expected)).CopyTo(destination);
            error = null;
            return true;
        }
        for (int i = 0; i < expected; i++)
        {
            if (!TryReadFloatComponent(i, out float f, out error))
                return false;
            MemoryMarshal.Write(destination.Slice(i * 4, 4), in f);
        }
        error = null;
        return true;
    }

    private bool TryWriteArray(ShaderTypeLayout targetType, Span<byte> destination, out string? error)
    {
        if (targetType.ElementType == null || targetType.ElementCount == 0 || targetType.StrideBytes == 0)
        {
            error = "Target array type has incomplete element layout.";
            return false;
        }
        int totalSrc = ElementCount * ComponentCount;
        if (totalSrc == 0 || totalSrc > targetType.ElementCount)
        {
            error = $"Array value has {totalSrc} items which cannot fit fixed array of length {targetType.ElementCount}.";
            return false;
        }
        ShaderTypeKind elemKind = targetType.ElementType.Kind;
        if (elemKind != ShaderTypeKind.Scalar)
        {
            error = $"Only scalar fixed arrays are supported by flat ShaderValue writes (target element kind: {elemKind}).";
            return false;
        }
        error = null;
        for (int i = 0; i < totalSrc; i++)
        {
            Span<byte> elemDst = destination.Slice(i * (int)targetType.StrideBytes, (int)targetType.ElementType.SizeBytes);
            if (!TryWriteSingleComponentAt(targetType.ElementType.ScalarType, targetType.ElementType.SizeBytes, i, elemDst, out error))
                return false;
        }
        return true;
    }

    private bool TryWriteSingleComponentAt(ShaderScalarType scalarType, uint sizeBytes, int index, Span<byte> destination, out string? error)
    {
        switch (scalarType)
        {
            case ShaderScalarType.Float16:
            case ShaderScalarType.Float32:
            case ShaderScalarType.Float64:
                if (!TryReadFloatComponent(index, out float f, out error))
                    return false;
                if (scalarType == ShaderScalarType.Float16)
                    MemoryMarshal.Write(destination, (Half)f);
                else if (scalarType == ShaderScalarType.Float32)
                    MemoryMarshal.Write(destination, f);
                else
                    MemoryMarshal.Write(destination, (double)f);
                return true;

            case ShaderScalarType.Int8:
            case ShaderScalarType.Int16:
            case ShaderScalarType.Int32:
            case ShaderScalarType.Int64:
                if (!TryReadSignedScalar(index, out long iv, out error))
                    return false;
                return WriteSignedScalar(scalarType, iv, destination, out error);

            case ShaderScalarType.UInt8:
            case ShaderScalarType.UInt16:
            case ShaderScalarType.UInt32:
            case ShaderScalarType.UInt64:
                if (!TryReadUnsignedScalar(index, out ulong uv, out error))
                    return false;
                return WriteUnsignedScalar(scalarType, uv, destination, out error);

            case ShaderScalarType.Bool:
                if (!TryReadSignedScalar(index, out long bv, out error))
                    return false;
                if (sizeBytes == 1)
                    destination[0] = bv != 0 ? (byte)1 : (byte)0;
                else
                    MemoryMarshal.Write(destination, bv != 0 ? 1u : 0u);
                return true;

            default:
                error = $"Unsupported scalar element type '{scalarType}'.";
                return false;
        }
    }

    private bool TryReadFloatComponent(int index, out float value, out string? error)
    {
        error = null;
        if (Kind == ShaderValueKind.Float32 && _floats != null)
        {
            value = _floats[index];
            return true;
        }
        if (Kind == ShaderValueKind.Int32 && _ints != null)
        {
            value = _ints[index];
            return true;
        }
        if (Kind == ShaderValueKind.UInt32 && _ints != null)
        {
            value = unchecked((uint)_ints[index]);
            return true;
        }
        value = 0f;
        error = $"Cannot convert {Kind} to float.";
        return false;
    }

    private bool TryReadSignedScalar(int index, out long value, out string? error)
    {
        error = null;
        if (Kind is ShaderValueKind.Int32 or ShaderValueKind.Bool32 && _ints != null)
        {
            value = _ints[index];
            return true;
        }
        if (Kind == ShaderValueKind.UInt32 && _ints != null)
        {
            value = unchecked((uint)_ints[index]);
            return true;
        }
        if (Kind == ShaderValueKind.Float32 && _floats != null)
        {
            float f = _floats[index];
            const float minSignedFloat = -9223372036854775808.0f; // -2^63 (exact in IEEE-754)
            const float maxSignedFloatExclusive = 9223372036854775808.0f; // +2^63 ((float)long.MaxValue rounds up to this)
            if (float.IsFinite(f) && MathF.Truncate(f) == f && f >= minSignedFloat && f < maxSignedFloatExclusive)
            {
                value = (long)f;
                return true;
            }
        }
        value = 0;
        error = $"Cannot convert {Kind} value to signed integer.";
        return false;
    }

    private bool TryReadUnsignedScalar(int index, out ulong value, out string? error)
    {
        error = null;
        if (Kind is ShaderValueKind.UInt32 && _ints != null)
        {
            value = unchecked((uint)_ints[index]);
            return true;
        }
        if (Kind is ShaderValueKind.Int32 or ShaderValueKind.Bool32 && _ints != null)
        {
            int iv = _ints[index];
            if (iv >= 0)
            {
                value = (uint)iv;
                return true;
            }
            value = 0;
            error = $"Negative integer value {iv} cannot convert to unsigned integer.";
            return false;
        }
        if (Kind == ShaderValueKind.Float32 && _floats != null)
        {
            float f = _floats[index];
            const float maxUnsignedFloatExclusive = 18446744073709551616.0f; // 2^64 ((float)ulong.MaxValue rounds up to this)
            if (float.IsFinite(f) && f >= 0f && MathF.Truncate(f) == f && f < maxUnsignedFloatExclusive)
            {
                value = (ulong)f;
                return true;
            }
        }
        value = 0;
        error = $"Cannot convert {Kind} value to unsigned integer.";
        return false;
    }

    private static bool WriteSignedScalar(ShaderScalarType scalarType, long value, Span<byte> destination, out string? error)
    {
        error = null;
        switch (scalarType)
        {
            case ShaderScalarType.Int8:
                if (value is < sbyte.MinValue or > sbyte.MaxValue) { error = $"Value {value} out of sbyte range."; return false; }
                destination[0] = unchecked((byte)(sbyte)value);
                return true;
            case ShaderScalarType.Int16:
                if (value is < short.MinValue or > short.MaxValue) { error = $"Value {value} out of short range."; return false; }
                MemoryMarshal.Write(destination, (short)value);
                return true;
            case ShaderScalarType.Int32:
                if (value is < int.MinValue or > int.MaxValue) { error = $"Value {value} out of int range."; return false; }
                MemoryMarshal.Write(destination, (int)value);
                return true;
            case ShaderScalarType.Int64:
                MemoryMarshal.Write(destination, value);
                return true;
            default:
                error = $"Unexpected signed type '{scalarType}'.";
                return false;
        }
    }

    private static bool WriteUnsignedScalar(ShaderScalarType scalarType, ulong value, Span<byte> destination, out string? error)
    {
        error = null;
        switch (scalarType)
        {
            case ShaderScalarType.UInt8:
                if (value > byte.MaxValue) { error = $"Value {value} out of byte range."; return false; }
                destination[0] = (byte)value;
                return true;
            case ShaderScalarType.UInt16:
                if (value > ushort.MaxValue) { error = $"Value {value} out of ushort range."; return false; }
                MemoryMarshal.Write(destination, (ushort)value);
                return true;
            case ShaderScalarType.UInt32:
                if (value > uint.MaxValue) { error = $"Value {value} out of uint range."; return false; }
                MemoryMarshal.Write(destination, (uint)value);
                return true;
            case ShaderScalarType.UInt64:
                MemoryMarshal.Write(destination, value);
                return true;
            default:
                error = $"Unexpected unsigned type '{scalarType}'.";
                return false;
        }
    }
}
