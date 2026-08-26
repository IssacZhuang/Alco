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
        : base(renderingSystem, (uint)blockSize(block), name)
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
    /// the same-width managed types (e.g. <c>uint</c> ↔ <c>uint</c>).
    /// </summary>
    /// <param name="name">The member name, as reflected.</param>
    /// <param name="value">The value to write (blitted raw, no reinterpretation).</param>
    /// <exception cref="KeyNotFoundException">No member of that name exists.</exception>
    /// <exception cref="ArgumentException">The value's size does not fit the member.</exception>
    public void SetValue<T>(string name, T value) where T : unmanaged
    {
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
    /// <exception cref="ArgumentException">The element count or element size does not match the member.</exception>
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
    private static int blockSize(ShaderUniformBlock block)
    {
        uint size = 0;
        for (int i = 0; i < block.Members.Count; i++)
        {
            size = Math.Max(size, block.Members[i].OffsetBytes + block.Members[i].SizeBytes);
        }
        return (int)((size + 15u) & ~15u);
    }
}
