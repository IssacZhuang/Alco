using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>
/// A bind group layout (the C# GPUBindGroup concept): a native layout of
/// binding declarations without bound resources.
/// </summary>
internal sealed unsafe class AlcoGpuBindGroup : GPUBindGroup
{
    #region Properties
    private readonly AlcoHandle _native;
    private readonly BindGroupEntry[] _bindings;

    #endregion

    #region Abstract Implementation
    /// <inheritdoc />
    public override IReadOnlyList<BindGroupEntry> Bindings
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bindings;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_native.IsNull && ((AlcoGpuDevice)Device).IsNativeAlive)
        {
            AlcoGpuNative.BindGroupLayoutDestroy(((AlcoGpuDevice)Device).Native, _native);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    /// <summary>Gets the native bind group layout handle.</summary>
    public AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    protected override GPUDevice Device { get; }

    internal AlcoGpuBindGroup(AlcoGpuDevice device, BindGroupDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        BindGroupEntry[] entries = descriptor.Bindings;
        AlcoBindGroupLayoutEntry* nativeEntries = AlcoGpuUtility.AllocBindGroupLayoutEntries(entries);

        try
        {
            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (byte* ptrName = name)
            {
                AlcoBindGroupLayoutDesc nativeDescriptor = new()
                {
                    Entries = nativeEntries,
                    EntryCount = (uint)entries.Length,
                    Name = ptrName,
                };

                AlcoGpuNative.BindGroupLayoutCreate(device.Native, in nativeDescriptor, out _native);
            }
        }
        finally
        {
            Free(nativeEntries);
        }

        _bindings = new BindGroupEntry[entries.Length];
        Array.Copy(entries, _bindings, entries.Length);
    }

    #endregion
}
