using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// UniformGraphicsBuffer: a uniform buffer whose layout comes from reflection,
// not from a hand-written C# twin struct. The caller sets members by name —
// the CPU/GPU alignment contract (offsets, 32-bit scalar kinds, array spans)
// is enforced by the ShaderUniformBlock the buffer was built from, so shader
// blocks may mix float/int/uint/bool members and arrays freely while the C#
// side stays readable (SetValue("levelIndex", 3) instead of packing
// Vector4.Params.x). Pass nodes get reflection from ShaderLibrary.GetReflection
// (module state) or a linked shader's reflection; either view works — the
// buffer only consumes the block vocabulary.
//
// Writes stage into a CPU-side buffer and flush lazily: the first EntryReadonly
// access (bind-group assembly) uploads dirty spans once per frame, mirroring
// GraphicsValueBuffer's write-then-bind rhythm without per-set uploads.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A reflection-driven uniform buffer: created from a <see cref="ShaderUniformBlock"/>,
/// written by member name, flushed to the GPU lazily on first bind after a change.
/// </summary>
public sealed unsafe class UniformGraphicsBuffer : GraphicsBuffer
{
    private readonly byte[] _staging;
    private readonly FrozenDictionary<string, ShaderUniformMember> _members;
    private readonly string _blockName;
    private bool _dirty;

    internal UniformGraphicsBuffer(
        RenderingSystem renderingSystem, ShaderUniformBlock block, string name)
        : base(renderingSystem, (uint)BlockSize(block), name)
    {
        if (block.UnsupportedMemberReason != null)
        {
            throw new NotSupportedException(
                $"Uniform buffer '{name}' is built from block '{block.Name}' that {block.UnsupportedMemberReason}");
        }
        _blockName = block.Name;
        _staging = new byte[Size];
        _members = block.Members.ToFrozenDictionary(member => member.Name, member => member);
    }

    /// <summary>The name of the block this buffer mirrors.</summary>
    public string BlockName => _blockName;

    /// <summary>
    /// Writes one member by name, staging the value at the member's reflected
    /// offset. A scalar/vector/matrix member takes its matching unmanaged type
    /// (e.g. <c>float3</c> ↔ <c>Vector3</c>); integer members marshal through
    /// the same-width managed types (e.g. <c>uint</c> ↔ <c>uint</c>), and
    /// <see langword="bool"/> marshals to the GPU's 4-byte 0/1. The value's
    /// scalar kind must match the member's reflected type — an <see langword="int"/>
    /// written to a <c>float</c> member is a silent garbage reinterpretation, so
    /// it throws instead.
    /// </summary>
    /// <param name="name">The member name, as reflected.</param>
    /// <param name="value">The value to write (blitted raw once the kind matches).</param>
    /// <exception cref="KeyNotFoundException">No member of that name exists.</exception>
    /// <exception cref="ArgumentException">The value's kind or size does not match the member.</exception>
    public void SetValue<T>(string name, T value) where T : unmanaged
    {
        if (typeof(T) == typeof(bool))
        {
            // Managed bool is 1 byte; the GPU slot is a 4-byte 0/1. Marshal
            // explicitly instead of blitting a partial byte.
            ShaderUniformMember member = Resolve(name);
            if (member.ScalarType != ShaderUniformScalarType.Bool32 || member.ComponentCount != 1)
            {
                throw new ArgumentException(
                    $"Uniform member '{name}' of block '{_blockName}' is {member.ScalarType}; a bool value needs a bool shader member.");
            }
            uint flag = Unsafe.As<T, bool>(ref value) ? 1u : 0u;
            ReadOnlySpan<byte> flagBytes;
            unsafe
            {
                flagBytes = new ReadOnlySpan<byte>(&flag, sizeof(uint));
            }
            SetSpan(name, flagBytes);
            return;
        }
        ValidateElement<T>(name);
        ReadOnlySpan<byte> bytes;
        unsafe
        {
            bytes = new ReadOnlySpan<byte>(&value, sizeof(T));
        }
        SetSpan(name, bytes);
    }

    /// <summary>
    /// Writes an array member by name: the elements land contiguously at the
    /// member's reflected offset, filling the array's whole span (fewer
    /// elements is an error — pad with default values explicitly).
    /// </summary>
    /// <param name="name">The array member name, as reflected.</param>
    /// <param name="values">The elements, exactly <see cref="ShaderUniformMember.ElementCount"/> of them.</param>
    /// <exception cref="KeyNotFoundException">No member of that name exists.</exception>
    /// <exception cref="ArgumentException">The element count, kind or size does not match the member.</exception>
    /// <exception cref="NotSupportedException">The element type needs per-element marshaling (<see langword="bool"/> arrays).</exception>
    public void SetValues<T>(string name, ReadOnlySpan<T> values) where T : unmanaged
    {
        ShaderUniformMember member = Resolve(name);
        if (member.ElementCount <= 1)
        {
            throw new ArgumentException(
                $"Uniform member '{name}' of block '{_blockName}' is not an array; use SetValue.");
        }
        if (values.Length != member.ElementCount)
        {
            throw new ArgumentException(
                $"Uniform member '{name}' of block '{_blockName}' takes exactly {member.ElementCount} elements, got {values.Length}.");
        }
        if (typeof(T) == typeof(bool))
        {
            throw new NotSupportedException(
                $"Uniform member '{name}' of block '{_blockName}' is a bool array; write it through a uint array instead.");
        }
        ValidateElement<T>(name);
        uint elementBytes = (uint)(sizeof(T) * values.Length);
        ReadOnlySpan<byte> bytes;
        unsafe
        {
            fixed (T* ptr = values)
            {
                bytes = new ReadOnlySpan<byte>(ptr, (int)elementBytes);
            }
        }
        SetSpan(name, bytes);
    }

    // One element of T must be exactly one reflected element of the member and
    // carry the same scalar kind — same-width blits across kinds (int → float)
    // are silent garbage, so they fail loudly with the fix spelled out.
    private void ValidateElement<T>(string name)
    {
        ShaderUniformMember member = Resolve(name);
        int elementBytes = member.ComponentCount * 4;
        if (sizeof(T) != elementBytes)
        {
            throw new ArgumentException(
                $"Uniform member '{name}' of block '{_blockName}' is {member.ComponentCount} component(s) ({elementBytes} bytes per element); the value's {sizeof(T)} bytes do not match.");
        }
        ShaderUniformScalarType? kind = typeof(T) switch
        {
            Type t when t == typeof(float) || t == typeof(Vector2) || t == typeof(Vector3)
                || t == typeof(Vector4) || t == typeof(Matrix4x4) => ShaderUniformScalarType.Float32,
            Type t when t == typeof(int) => ShaderUniformScalarType.Int32,
            Type t when t == typeof(uint) => ShaderUniformScalarType.UInt32,
            _ => null,
        };
        if (kind is { } valueKind && valueKind != member.ScalarType)
        {
            throw new ArgumentException(
                $"Uniform member '{name}' of block '{_blockName}' is {member.ScalarType} but the value is {valueKind}; " +
                "convert the value or retype the shader member so both sides agree.");
        }
    }

    /// <summary>
    /// The bind group entry (uniform); flushing any staged writes first. The
    /// base class's group caching still applies — one group per layout for the
    /// buffer's lifetime.
    /// </summary>
    public override GPUResourceGroup EntryReadonly
    {
        get
        {
            Flush();
            return base.EntryReadonly;
        }
    }

    /// <summary>
    /// Uploads the staged bytes once per change-set: one whole-buffer write,
    /// skipping the no-op when nothing was set since the last flush. Uniform
    /// blocks are small (a few hundred bytes at most); a whole-buffer write
    /// beats span tracking and needs no queue write batching.
    /// </summary>
    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }
        fixed (byte* ptr = _staging)
        {
            _device.WriteBuffer(_buffer, 0, ptr, Size);
        }
        _dirty = false;
    }

    /// <summary>
    /// Reads one staged float slot back — the test seam for asserting the
    /// name-keyed write contract (NoGPU makes the upload itself a no-op).
    /// </summary>
    internal unsafe float ReadStagingFloat(uint offset)
        => BitConverter.ToSingle(_staging, (int)offset);

    private void SetSpan(string name, ReadOnlySpan<byte> bytes)
    {
        ShaderUniformMember member = Resolve(name);
        if (bytes.Length > member.SizeBytes)
        {
            throw new ArgumentException(
                $"Value of {bytes.Length} bytes does not fit uniform member '{name}' of block '{_blockName}' ({member.SizeBytes} bytes).");
        }
        bytes.CopyTo(_staging.AsSpan((int)member.OffsetBytes));
        _dirty = true;
    }

    private ShaderUniformMember Resolve(string name)
        => _members.TryGetValue(name, out ShaderUniformMember member)
            ? member
            : throw new KeyNotFoundException(
                $"Uniform member '{name}' not found in block '{_blockName}'; expected one of: {string.Join(", ", _members.Keys)}.");

    // The GPU block size, 16-byte aligned like every uniform binding.
    private static int BlockSize(ShaderUniformBlock block)
    {
        uint size = 0;
        for (int i = 0; i < block.Members.Count; i++)
        {
            size = Math.Max(size, block.Members[i].OffsetBytes + block.Members[i].SizeBytes);
        }
        return (int)((size + 15u) & ~15u);
    }
}
