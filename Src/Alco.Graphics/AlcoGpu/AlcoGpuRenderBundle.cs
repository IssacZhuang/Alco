using System.Diagnostics;
using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

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
        // the bundle will not be released if End() is not called
        // do check here to prevent memory leak
        ReleaseRenderBundle();
        ReleaseRenderBundleEncoder();

        InteropUtility.Free(_nativeName);
    }

    /// <summary>Begins the native render bundle encoder.</summary>
    protected unsafe override void BeginCore(GPUAttachmentLayout attachmentLayout)
    {
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
            // Must match the read-only state of the passes this bundle is executed
            // into (bundle/pass attachment compatibility).
            DepthReadOnly = depthInfo.HasValue && depthInfo.Value.IsDepthReadOnly ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
            StencilReadOnly = depthInfo.HasValue && depthInfo.Value.IsStencilReadOnly ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
            SampleCount = 1,
            Name = _nativeName,
        };

        uint status = AlcoGpuNative.BundleEncoderCreate(_device.Native, in descriptor, out _bundleEncoder);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    /// <summary>Ends the render bundle encoder and finishes the encoded bundle.</summary>
    protected unsafe override void EndCore()
    {
        ReleaseRenderBundle();

        uint status = AlcoGpuNative.BundleEncoderFinish(_device.Native, _bundleEncoder, out _bundle);
        if (status != AlcoGpuAbi.Status.Ok)
        {
            ReleaseRenderBundleEncoder();
            AlcoGpuMarshal.ThrowIfFailed(status);
        }

        // finish consumes the encoder handle
        _bundleEncoder = AlcoHandle.Null;
        _graphicsPipeline = AlcoHandle.Null;
    }

    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        _graphicsPipeline = ((AlcoGpuGraphicsPipeline)pipeline).Native;
        uint status = AlcoGpuNative.BundleSetPipeline(_device.Native, _bundleEncoder, _graphicsPipeline);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleSetBindGroup(
            _device.Native, _bundleEncoder, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleSetVertexBuffer(
            _device.Native, _bundleEncoder, slot, ((AlcoGpuBuffer)buffer).Native, offset, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleSetIndexBuffer(
            _device.Native, _bundleEncoder, ((AlcoGpuBuffer)buffer).Native, (uint)format, offset, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleDraw(_device.Native, _bundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleDrawIndexed(_device.Native, _bundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleDrawIndirect(
            _device.Native, _bundleEncoder, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleDrawIndexedIndirect(
            _device.Native, _bundleEncoder, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        ValidateGraphicsPipeline();

        uint status = AlcoGpuNative.BundleSetImmediates(_device.Native, _bundleEncoder, bufferOffset, data, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
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
        if (!_bundle.IsNull)
        {
            uint status = AlcoGpuNative.RenderBundleDestroy(_device.Native, _bundle);
            AlcoGpuMarshal.ThrowIfFailed(status);
            _bundle = AlcoHandle.Null;
        }
    }

    private void ReleaseRenderBundleEncoder()
    {
        if (!_bundleEncoder.IsNull)
        {
            // An unfinished bundle encoder is simply destroyed without finishing.
            uint status = AlcoGpuNative.BundleEncoderDestroy(_device.Native, _bundleEncoder);
            AlcoGpuMarshal.ThrowIfFailed(status);
            _bundleEncoder = AlcoHandle.Null;
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
