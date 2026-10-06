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
    private AlcoGpuAbi.BindGroupLayoutHandle _native;
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
        try
        {
            AlcoGpuAbi.BindGroupLayoutHandle handle = _native;
            _native = AlcoGpuAbi.BindGroupLayoutHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.BindGroupLayoutDestroy(handle);
                }
                finally
                {
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    /// <summary>Gets the native bind group layout handle.</summary>
    public AlcoGpuAbi.BindGroupLayoutHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    protected override GPUDevice Device { get; }

    internal AlcoGpuBindGroup(AlcoGpuDevice device, BindGroupDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;

            BindGroupEntry[] entries = descriptor.Bindings;
            _bindings = (BindGroupEntry[])entries.Clone();
            Span<AlcoGpuAbi.BindGroupLayoutEntry> nativeEntryStorage = entries.Length <= 32
                ? stackalloc AlcoGpuAbi.BindGroupLayoutEntry[entries.Length] : new AlcoGpuAbi.BindGroupLayoutEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                nativeEntryStorage[i] = AlcoGpuUtility.ConvertBindGroupLayoutEntry(entries[i]);
            }

            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (AlcoGpuAbi.BindGroupLayoutEntry* nativeEntries = nativeEntryStorage)
            {
                fixed (byte* ptrName = name)
                {
                    AlcoGpuAbi.BindGroupLayoutDesc nativeDescriptor = new()
                    {
                        Entries = nativeEntries,
                        EntryCount = (uint)entries.Length,
                        Name = ptrName,
                    };

                    AlcoGpuNative.BindGroupLayoutCreate(device.Native, in nativeDescriptor, out _native);
                }
            }


        }
        catch
        {
            try { Destroy(false); }
            catch { /* Preserve the construction failure. */ }
            throw;
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(device);
        }
    }

    #endregion
}
