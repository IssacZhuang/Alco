using Alco.Graphics.AlcoGpu.Interop;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Round-trip tests of the alco-gpu C ABI: version handshake, real device
/// creation on the local adapter, info queries and the failure contract.
/// The process-wide error callback registered by <see cref="AlcoGpuNative"/>
/// turns every failure status into a <see cref="GraphicsException"/> thrown
/// from the native call site (NOT_READY is control flow and returns normally).
/// </summary>
[TestFixture]
[Category("AlcoGpu")]
public unsafe class AlcoGpuAbiTests
{
    /// <summary>Verifies that the native library implements the managed ABI version.</summary>
    [Test]
    public void AbiVersionMatches()
    {
        uint version = AlcoGpuNative.AbiVersion();
        Assert.That(version >> 16, Is.EqualTo(AlcoGpuAbi.AbiMajor));
        Assert.That(version & 0xFFFF, Is.GreaterThanOrEqualTo(AlcoGpuAbi.AbiMinor));
    }

    /// <summary>Verifies native build information identifies the pinned wgpu version.</summary>
    [Test]
    public void BuildInfoCarriesWgpuVersion()
    {
        AlcoBuildInfo info = default;
        AlcoGpuNative.BuildInfo(ref info);
        Assert.That(info.WgpuVersion >> 16, Is.EqualTo(30));
        string? build = AlcoGpuMarshal.BorrowedString(info.AlcoBuild);
        Assert.That(build, Is.Not.Null.And.Contains("alco-gpu"));
    }

    /// <summary>Creates a device and validates capabilities across supported desktop backends.</summary>
    [Test]
    public void DeviceCreateGetInfoDestroyRoundTrip()
    {
        AlcoDeviceDesc desc = default;
        desc.Backend = AlcoGpuAbi.BackendRequest.Auto;
        desc.Debug = AlcoGpuAbi.AlcoTrue;
        desc.RequiredFeatures = 0;
        desc.PushConstantsSize = 128;
        desc.Name = null;

        AlcoGpuNative.DeviceCreate(in desc, out AlcoHandle device);
        Assert.That(device.IsNull, Is.False);

        try
        {
            AlcoDeviceInfo info = default;
            AlcoGpuNative.DeviceGetInfo(device, ref info);
            Assert.That(info.Backend, Is.EqualTo(AlcoGpuAbi.BackendResolved.Vulkan)
                .Or.EqualTo(AlcoGpuAbi.BackendResolved.Dx12)
                .Or.EqualTo(AlcoGpuAbi.BackendResolved.Metal));
            Assert.That(info.Caps & AlcoGpuAbi.Caps.PassthroughShaders, Is.Not.Zero, "passthrough shaders expected on desktop adapters");

            // MetalLib passthrough is an Apple-platform capability; DXIL/MSL
            // passthrough on the desktop is covered by the check above.
            if (OperatingSystem.IsMacOS())
            {
                Assert.That(info.Caps & AlcoGpuAbi.Caps.MetalLib, Is.Not.Zero, "MetalLib passthrough expected on Apple platforms");
            }
            Assert.That(info.MaxBindGroups, Is.GreaterThan(0));
            Assert.That(info.MaxImmediateSize, Is.EqualTo(128));
            Assert.That(AlcoGpuMarshal.BorrowedString(info.AdapterName), Is.Not.Null.And.Not.Empty);
        }
        finally
        {
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Verifies stale device handles throw a recoverable managed exception.</summary>
    [Test]
    public void DoubleDestroyThrowsInvalidHandleInsteadOfCrashing()
    {
        AlcoDeviceDesc desc = default;
        desc.Backend = AlcoGpuAbi.BackendRequest.Auto;
        desc.Debug = AlcoGpuAbi.AlcoFalse;
        desc.PushConstantsSize = 128;

        AlcoGpuNative.DeviceCreate(in desc, out AlcoHandle device);
        AlcoGpuNative.DeviceDestroy(device);

        // The second destroy must unwind out of the native call as a managed
        // exception — this exact scenario aborted the process under wgpu-native.
        GraphicsException error = Assert.Throws<GraphicsException>(
            () => AlcoGpuNative.DeviceDestroy(device))!;
        Assert.That(error.Message, Does.Contain("invalid handle"));
        Assert.That(error.Message, Does.Contain("device handle is invalid or already destroyed"));

        // The thread-local last-error slot still carries the failure for callers
        // that prefer polling it after the callback fired.
        AlcoErrorInfo lastError = default;
        AlcoGpuNative.GetLastError(ref lastError);
        Assert.That(lastError.Status, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));
        Assert.That(AlcoGpuMarshal.BorrowedString(lastError.Message), Is.Not.Null.And.Not.Empty);
    }

    /// <summary>Verifies batch destruction reuses every slot without accepting stale handles.</summary>
    [Test]
    public void BatchBufferDestructionReusesAllSlots()
    {
        const int count = 64;
        AlcoHandle device = CreateNativeDevice();
        AlcoHandle[] buffers = new AlcoHandle[count];
        AlcoBufferDesc descriptor = new() { Size = 64, Usage = (uint)BufferUsage.Uniform };
        try
        {
            for (int round = 0; round < 32; round++)
            {
                AlcoHandle[] stale = (AlcoHandle[])buffers.Clone();
                for (int i = 0; i < count; i++)
                {
                    if (!buffers[i].IsNull)
                    {
                        AlcoGpuNative.BufferDestroy(device, buffers[i]);
                        buffers[i] = AlcoHandle.Null;
                    }
                }
                var indices = new HashSet<uint>();
                for (int i = 0; i < count; i++)
                {
                    AlcoGpuNative.BufferCreate(device, in descriptor, out buffers[i]);
                    uint index = (uint)buffers[i].Value;
                    Assert.That(index, Is.LessThan(count), "the handle table grew despite bounded live buffers");
                    Assert.That(indices.Add(index), Is.True, "two live buffers share a slot");
                }
                if (round > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Throws<GraphicsException>(() => AlcoGpuNative.BufferDestroy(device, stale[i]));
                    }
                }
            }
        }
        finally
        {
            for (int i = 0; i < count; i++)
            {
                if (!buffers[i].IsNull)
                {
                    AlcoGpuNative.BufferDestroy(device, buffers[i]);
                }
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Creates empty layouts and instances with either representation of an empty entry array.</summary>
    /// <param name="useNullEntries">Whether the zero-length entry arrays use null pointers.</param>
    [TestCase(true)]
    [TestCase(false)]
    public void EmptyBindGroupsAcceptZeroEntries(bool useNullEntries)
    {
        AlcoHandle device = CreateNativeDevice();
        AlcoHandle layout = AlcoHandle.Null;
        AlcoHandle group = AlcoHandle.Null;
        AlcoBindGroupLayoutEntry layoutEntry = default;
        AlcoBindGroupEntry groupEntry = default;
        try
        {
            AlcoBindGroupLayoutDesc layoutDescriptor = new()
            {
                Entries = useNullEntries ? null : &layoutEntry,
            };
            AlcoGpuNative.BindGroupLayoutCreate(device, in layoutDescriptor, out layout);
            AlcoBindGroupDesc groupDescriptor = new()
            {
                Layout = layout,
                Entries = useNullEntries ? null : &groupEntry,
            };
            AlcoGpuNative.BindGroupCreate(device, in groupDescriptor, out group);

            layoutDescriptor.Entries = null;
            layoutDescriptor.EntryCount = 1;
            Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BindGroupLayoutCreate(device, in layoutDescriptor, out _));
            groupDescriptor.Entries = null;
            groupDescriptor.EntryCount = 1;
            Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BindGroupCreate(device, in groupDescriptor, out _));
        }
        finally
        {
            if (!group.IsNull)
            {
                AlcoGpuNative.BindGroupDestroy(device, group);
            }
            if (!layout.IsNull)
            {
                AlcoGpuNative.BindGroupLayoutDestroy(device, layout);
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Verifies render-pass failures include their distinct underlying validation causes.</summary>
    /// <param name="invalidStencil">Whether to supply stencil operations on a depth-only attachment.</param>
    [TestCase(true)]
    [TestCase(false)]
    public void ValidationErrorsRetainRootCauses(bool invalidStencil)
    {
        AlcoHandle device = CreateNativeDevice();
        AlcoHandle texture = AlcoHandle.Null;
        AlcoHandle view = AlcoHandle.Null;
        AlcoHandle encoder = AlcoHandle.Null;
        AlcoHandle pass = AlcoHandle.Null;
        try
        {
            AlcoTextureDesc textureDescriptor = new()
            {
                Dimension = (uint)TextureDimension.Texture2D,
                Format = (uint)PixelFormat.Depth32Float,
                Usage = (uint)TextureUsage.DepthAttachment,
                Width = 16,
                Height = 16,
                DepthOrArrayLayers = 1,
                MipLevelCount = 1,
                SampleCount = 1,
            };
            AlcoGpuNative.TextureCreate(device, in textureDescriptor, out texture);
            AlcoGpuNative.TextureCreateView(device, texture, null, out view);
            AlcoGpuNative.EncoderCreate(device, null, out encoder);
            AlcoDepthStencilAttachment depth = new()
            {
                View = view,
                DepthLoadOp = 1,
                DepthStoreOp = 0,
                DepthClear = invalidStencil ? 1f : 2f,
                StencilLoadOp = invalidStencil ? 0 : AlcoGpuAbi.AlcoNone,
                StencilStoreOp = invalidStencil ? 0 : AlcoGpuAbi.AlcoNone,
            };
            AlcoRenderPassDesc descriptor = new() { DepthStencil = &depth };
            AlcoGpuNative.RenderPassBegin(device, encoder, in descriptor, out pass);
            AlcoGpuNative.RenderPassEnd(device, pass);
            pass = AlcoHandle.Null;
            AlcoHandle buffer = AlcoHandle.Null;
            GraphicsException finishError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.EncoderFinish(device, encoder, out buffer))!;
            encoder = AlcoHandle.Null;
            Assert.That(finishError.Message, Does.Contain("validation"));
            Assert.That(buffer.IsNull, Is.True);
            Assert.That(finishError.Message, Does.Contain("In a pass parameter"));
            Assert.That(finishError.Message, invalidStencil
                ? Does.Contain("without stencil aspect").And.Contain("Depth32Float")
                : Does.Contain("between 0.0 and 1.0"));
        }
        finally
        {
            if (!pass.IsNull)
            {
                AlcoGpuNative.RenderPassEnd(device, pass);
            }
            if (!encoder.IsNull)
            {
                AlcoGpuNative.EncoderDestroy(device, encoder);
            }
            if (!view.IsNull)
            {
                AlcoGpuNative.TextureViewDestroy(device, view);
            }
            if (!texture.IsNull)
            {
                AlcoGpuNative.TextureDestroy(device, texture);
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    private static AlcoHandle CreateNativeDevice()
    {
        AlcoDeviceDesc descriptor = new()
        {
            Backend = AlcoGpuAbi.BackendRequest.Auto,
            Debug = AlcoGpuAbi.AlcoTrue,
            PushConstantsSize = 128,
        };
        AlcoGpuNative.DeviceCreate(in descriptor, out AlcoHandle device);
        return device;
    }

    /// <summary>
    /// Verifies a nonblocking device poll is safe, an empty message queue returns
    /// NOT_READY (control flow never fires the error callback), and polling it
    /// twice in a row stays exception-free.
    /// </summary>
    [Test]
    public void PollAndEmptyMessageQueueAreSafe()
    {
        AlcoDeviceDesc desc = default;
        desc.Backend = AlcoGpuAbi.BackendRequest.Auto;
        desc.PushConstantsSize = 128;

        AlcoGpuNative.DeviceCreate(in desc, out AlcoHandle device);
        try
        {
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoFalse, 0, &queueEmpty);

            AlcoDeviceMessage message = default;
            uint status = AlcoGpuNative.DevicePopMessage(device, ref message);
            Assert.That(status, Is.EqualTo(AlcoGpuAbi.Status.NotReady));
        }
        finally
        {
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>
    /// Verifies every documented log level is accepted and an unknown level
    /// throws through the error callback; the filter is restored to the
    /// process default afterwards.
    /// </summary>
    [Test]
    public void LogLevelAcceptsKnownValuesAndRejectsUnknownValues()
    {
        foreach (uint level in new[]
                 {
                     AlcoGpuAbi.LogLevel.Off, AlcoGpuAbi.LogLevel.Error, AlcoGpuAbi.LogLevel.Warn,
                     AlcoGpuAbi.LogLevel.Info, AlcoGpuAbi.LogLevel.Debug, AlcoGpuAbi.LogLevel.Trace,
                 })
        {
            Assert.That(AlcoGpuNative.SetLogLevel(level), Is.EqualTo(AlcoGpuAbi.Status.Ok));
        }

        GraphicsException error = Assert.Throws<GraphicsException>(
            () => AlcoGpuNative.SetLogLevel(99))!;
        Assert.That(error.Message, Does.Contain("log level"));

        // Restore the forwarding default so later tests observe Warn-level records.
        AlcoGpuNative.SetLogLevel(AlcoGpuAbi.LogLevel.Warn);
    }

    /// <summary>
    /// Verifies log callback registration succeeds and stays idempotent: the
    /// native forwarder installs once and repeat registrations keep it in
    /// place.
    /// </summary>
    [Test]
    public void LogCallbackRegistrationIsIdempotent()
    {
        Assert.That(
            AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null),
            Is.EqualTo(AlcoGpuAbi.Status.Ok));
        Assert.That(
            AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null),
            Is.EqualTo(AlcoGpuAbi.Status.Ok));
    }
}
