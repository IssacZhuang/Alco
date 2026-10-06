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
    private AlcoGpuAbi.BundleEncoderHandle _bundleEncoder;
    private AlcoGpuAbi.RenderBundleHandle _bundle;

    private AlcoGpuAbi.GraphicsPipelineHandle _graphicsPipeline;

    // owned by this object, released on dispose
    private byte* _nativeName;

    #endregion

    #region Abstract Implementation

    /// <summary>Gets whether a finished native render bundle is available for execution.</summary>
    public override bool HasBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !_bundle.IsNull;
    }

    internal AlcoGpuAbi.RenderBundleHandle Native
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
            byte* name = _nativeName;
            _nativeName = null;
            InteropUtility.Free(name);
            _graphicsPipeline = AlcoGpuAbi.GraphicsPipelineHandle.Null;
            _isRecording = false;
        }
        failure?.Throw();
    }

    /// <summary>Begins the native render bundle encoder.</summary>
    protected unsafe override void BeginCore(GPUAttachmentLayout attachmentLayout)
    {
        try
        {
            _isRecording = false;
            ReleaseRenderBundleEncoder();
            AlcoGpuAttachmentLayout nativeAttachmentLayout = (AlcoGpuAttachmentLayout)attachmentLayout;

            int colorCount = nativeAttachmentLayout.ColorInfos.Length;
            Span<uint> colorStorage = colorCount <= 64 ? stackalloc uint[colorCount] : new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                colorStorage[i] = (uint)nativeAttachmentLayout.ColorInfos[i].Format;
            }

            DepthAttachmentInfo? depthInfo = nativeAttachmentLayout.DepthInfo;
            AlcoGpuAbi.BundleEncoderDesc descriptor = new()
            {
                ColorFormats = null,
                ColorFormatCount = (uint)colorCount,
                DepthStencilFormat = depthInfo.HasValue ? (uint)depthInfo.Value.Format : AlcoGpuAbi.None,
                // Missing aspects must be read-only too, matching pass channels with
                // omitted operations under core 30's bundle/pass compatibility rules.
                DepthReadOnly = depthInfo.HasValue && depthInfo.Value.IsDepthReadOnly ? AlcoGpuAbi.True : AlcoGpuAbi.False,
                StencilReadOnly = depthInfo.HasValue && depthInfo.Value.IsStencilReadOnly ? AlcoGpuAbi.True : AlcoGpuAbi.False,
                SampleCount = 1,
                Name = _nativeName,
            };

            fixed (uint* colors = colorStorage)
            {
                descriptor.ColorFormats = colors;
                AlcoGpuNative.BundleEncoderCreate(_device.Native, in descriptor, out _bundleEncoder);
            }
            _isRecording = true;
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(attachmentLayout);
        }
    }

    /// <summary>Ends the render bundle encoder and finishes the encoded bundle.</summary>
    protected unsafe override void EndCore()
    {
        try
        {
            ReleaseRenderBundle();

            // Finish consumes the encoder even when validation fails. Do not issue a
            // second destroy that would replace the actionable validation error.
            AlcoGpuAbi.BundleEncoderHandle bundleEncoder = _bundleEncoder;
            _bundleEncoder = AlcoGpuAbi.BundleEncoderHandle.Null;
            _graphicsPipeline = AlcoGpuAbi.GraphicsPipelineHandle.Null;
            AlcoGpuNative.BundleEncoderFinish(bundleEncoder, out _bundle);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
    {
        try
        {
            _graphicsPipeline = ((AlcoGpuGraphicsPipeline)pipeline).Native;
            AlcoGpuNative.BundleSetPipeline(_bundleEncoder, _graphicsPipeline);
            // The base recording list retains the resources while this encoder is alive.
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(pipeline);
        }
    }

    /// <inheritdoc />
    protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleSetBindGroup(_bundleEncoder, slot, ((AlcoGpuResourceGroup)resourceGroup).Native);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(resourceGroup);
        }
    }

    /// <inheritdoc />
    protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleSetVertexBuffer(_bundleEncoder, slot, ((AlcoGpuBuffer)buffer).Native, offset, size);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(buffer);
        }
    }

    /// <inheritdoc />
    protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleSetIndexBuffer(_bundleEncoder, ((AlcoGpuBuffer)buffer).Native, (uint)format, offset, size);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(buffer);
        }
    }

    /// <inheritdoc />
    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleDraw(_bundleEncoder, vertexCount, instanceCount, firstVertex, firstInstance);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleDrawIndexed(_bundleEncoder, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    /// <inheritdoc />
    protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleDrawIndirect(_bundleEncoder, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(indirectBuffer);
        }
    }

    /// <inheritdoc />
    protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleDrawIndexedIndirect(_bundleEncoder, ((AlcoGpuBuffer)indirectBuffer).Native, offset);
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(indirectBuffer);
        }
    }

    /// <inheritdoc />
    protected override unsafe void PushGraphicsConstantsCore(uint bufferOffset, byte* data, uint size)
    {
        try
        {
            ValidateGraphicsPipeline();

            AlcoGpuNative.BundleSetImmediates(_bundleEncoder, bufferOffset, data, size);
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    internal unsafe AlcoGpuRenderBundle(AlcoGpuDevice device, in RenderBundleDescriptor? descriptor) : base(descriptor)
    {
        Device = device;
        _device = device;

        _bundle = AlcoGpuAbi.RenderBundleHandle.Null;
        _bundleEncoder = AlcoGpuAbi.BundleEncoderHandle.Null;

        try
        {
            ReadOnlySpan<byte> nameSpan = Name.Utf8Z();
            fixed (byte* ptr = nameSpan)
            {
                _nativeName = InteropUtility.Alloc<byte>(nameSpan.Length);
                InteropUtility.Copy(ptr, _nativeName, (uint)nameSpan.Length, (uint)nameSpan.Length);
            }
        }
        catch
        {
            try { Destroy(false); }
            catch { /* Preserve the construction failure. */ }
            throw;
        }
    }

    private void ReleaseRenderBundle()
    {
        try
        {
            if (!_bundle.IsNull)
            {
                AlcoGpuAbi.RenderBundleHandle bundle = _bundle;
                _bundle = AlcoGpuAbi.RenderBundleHandle.Null;
                AlcoGpuNative.RenderBundleDestroy(bundle);
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    private void ReleaseRenderBundleEncoder()
    {
        try
        {
            if (!_bundleEncoder.IsNull)
            {
                // An unfinished bundle encoder is simply destroyed without finishing.
                AlcoGpuAbi.BundleEncoderHandle bundleEncoder = _bundleEncoder;
                _bundleEncoder = AlcoGpuAbi.BundleEncoderHandle.Null;
                AlcoGpuNative.BundleEncoderDestroy(bundleEncoder);
            }
        }
        finally
        {
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
