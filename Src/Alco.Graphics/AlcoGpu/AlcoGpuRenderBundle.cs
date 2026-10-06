using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Records reusable native rendering commands with call-scoped managed resource lifetimes.</summary>
internal sealed unsafe partial class AlcoGpuRenderBundle : GPURenderBundle
{
    private static readonly Exception ExceptionNoGraphicsPipeline = new("No graphics pipeline is set before drawing or set resources");

    #region Properties
    private readonly AlcoGpuDevice _device;
    private AlcoHandle _bundleEncoder;
    private AlcoHandle _bundle;

    private AlcoHandle _graphicsPipeline;

    // owned by this object, released on dispose
    private readonly byte* _nativeName;

    #endregion

    #region Abstract Implementation

    /// <summary>Gets whether a finished native render bundle is available for execution.</summary>
    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !_bundle.IsNull;
    }

    internal AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bundle;
    }

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        ExceptionDispatchInfo? failure = null;
        try
        {
            ReleaseRenderBundle();
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            ReleaseRenderBundleEncoder();
        }
        catch (Exception error)
        {
            failure ??= ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            InteropUtility.Free(_nativeName);
            _graphicsPipeline = AlcoHandle.Null;
            _isRecording = false;
        }
        failure?.Throw();
    }

    /// <summary>Begins the native render bundle encoder.</summary>
    protected unsafe override void BeginCore(GPUAttachmentLayout attachmentLayout)
    {
        _isRecording = false;
        ReleaseRenderBundleEncoder();
        AlcoGpuAttachmentLayout nativeAttachmentLayout = (AlcoGpuAttachmentLayout)attachmentLayout;

        int colorCount = nativeAttachmentLayout.ColorInfos.Length;
        uint* colors = stackalloc uint[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            colors[i] = (uint)nativeAttachmentLayout.ColorInfos[i].Format;
        }

        AlcoDepthAttachmentInfo? depthInfo = nativeAttachmentLayout.DepthInfo;
        AlcoBundleEncoderDesc descriptor = new()
        {
            ColorFormats = colors,
            ColorFormatCount = (uint)colorCount,
            DepthStencilFormat = depthInfo.HasValue ? (uint)depthInfo.Value.Format : AlcoGpuAbi.AlcoNone,
            // Missing aspects must be read-only too, matching pass channels with
            // omitted operations under core 30's bundle/pass compatibility rules.
            DepthReadOnly = depthInfo.HasValue && depthInfo.Value.IsDepthReadOnly ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
            StencilReadOnly = depthInfo.HasValue && depthInfo.Value.IsStencilReadOnly ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
            SampleCount = 1,
            Name = _nativeName,
        };

        AlcoGpuNative.BundleEncoderCreate(_device.Native, in descriptor, out _bundleEncoder);
        _isRecording = true;
    }

    /// <summary>Ends the render bundle encoder and finishes the encoded bundle.</summary>
    protected unsafe override void EndCore()
    {
        ReleaseRenderBundle();

        // Finish consumes the encoder even when validation fails. Do not issue a
        // second destroy that would replace the actionable validation error.
        AlcoHandle bundleEncoder = _bundleEncoder;
        _bundleEncoder = AlcoHandle.Null;
        _graphicsPipeline = AlcoHandle.Null;
        AlcoGpuNative.BundleEncoderFinish(_device.Native, bundleEncoder, out _bundle);
    }

    /// <inheritdoc />
    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        _graphicsPipeline = ((AlcoGpuGraphicsPipeline)pipeline).Native;
        AlcoGpuNative.BundleSetPipeline(_device.Native, _bundleEncoder, _graphicsPipeline);
        // The base recording list retains the resources while this encoder is alive.
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleSetBindGroup(
            _device.Native, _bundleEncoder, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleSetVertexBuffer(
            _device.Native, _bundleEncoder, slot, ((AlcoGpuBuffer)buffer).Native, offset, size);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleSetIndexBuffer(
            _device.Native, _bundleEncoder, ((AlcoGpuBuffer)buffer).Native, (uint)format, offset, size);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleDraw(_device.Native, _bundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleDrawIndexed(_device.Native, _bundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleDrawIndirect(
            _device.Native, _bundleEncoder, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleDrawIndexedIndirect(
            _device.Native, _bundleEncoder, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        GC.KeepAlive(this);
    }

    /// <inheritdoc />
    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        ValidateGraphicsPipeline();

        AlcoGpuNative.BundleSetImmediates(_device.Native, _bundleEncoder, bufferOffset, data, size);
        GC.KeepAlive(this);
    }

    #endregion

    #region AlcoGpu Implementation

    internal unsafe AlcoGpuRenderBundle(AlcoGpuDevice device, in RenderBundleDescriptor? descriptor) : base(descriptor)
    {
        Device = device;
        _device = device;

        _bundle = AlcoHandle.Null;
        _bundleEncoder = AlcoHandle.Null;

        ReadOnlySpan<byte> nameSpan = Name.Utf8Z();
        fixed (byte* ptr = nameSpan)
        {
            _nativeName = InteropUtility.Alloc<byte>(nameSpan.Length);
            InteropUtility.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
        }
    }

    private void ReleaseRenderBundle()
    {
        if (!_bundle.IsNull && _device.IsNativeAlive)
        {
            AlcoHandle bundle = _bundle;
            _bundle = AlcoHandle.Null;
            AlcoGpuNative.RenderBundleDestroy(_device.Native, bundle);
            GC.KeepAlive(this);
        }
    }

    private void ReleaseRenderBundleEncoder()
    {
        if (!_bundleEncoder.IsNull && _device.IsNativeAlive)
        {
            // An unfinished bundle encoder is simply destroyed without finishing.
            AlcoHandle bundleEncoder = _bundleEncoder;
            _bundleEncoder = AlcoHandle.Null;
            AlcoGpuNative.BundleEncoderDestroy(_device.Native, bundleEncoder);
            GC.KeepAlive(this);
        }
    }

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateGraphicsPipeline()
    {
        if (_graphicsPipeline.IsNull)
        {
            throw ExceptionNoGraphicsPipeline;
        }
    }

    #endregion
}
