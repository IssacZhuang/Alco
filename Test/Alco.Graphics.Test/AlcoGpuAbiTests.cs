using System.Reflection;
using System.Runtime.InteropServices;
using Alco.Graphics.AlcoGpu.Interop;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Round-trip tests of the opaque-pointer alco-gpu C ABI, including typed layouts,
/// real GPU copies, device capabilities, validation causes, and the failure contract.
/// Unsupported stale, fabricated, incorrectly typed, and double-freed pointers are never used.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
public unsafe class AlcoGpuAbiTests
{
    /// <summary>Verifies that both sides implement the ABI 2 version handshake.</summary>
    [Test]
    public void AbiVersionMatches()
    {
        Assert.That(AlcoGpuAbi.AbiMajor, Is.EqualTo(2));
        Assert.That(AlcoGpuAbi.AbiMinor, Is.EqualTo(3));
        uint version = AlcoGpuNative.AbiVersion();
        Assert.That(version >> 16, Is.EqualTo(AlcoGpuAbi.AbiMajor));
        Assert.That(version & 0xFFFF, Is.GreaterThanOrEqualTo(AlcoGpuAbi.AbiMinor));
    }

    /// <summary>Verifies every typed handle contains exactly one native-sized pointer.</summary>
    /// <param name="handleType">The managed handle type whose native layout is checked.</param>
    [TestCase(typeof(AlcoDeviceHandle))]
    [TestCase(typeof(AlcoBufferHandle))]
    [TestCase(typeof(AlcoTextureHandle))]
    [TestCase(typeof(AlcoTextureViewHandle))]
    [TestCase(typeof(AlcoSamplerHandle))]
    [TestCase(typeof(AlcoShaderModuleHandle))]
    [TestCase(typeof(AlcoBindGroupLayoutHandle))]
    [TestCase(typeof(AlcoBindGroupHandle))]
    [TestCase(typeof(AlcoQuerySetHandle))]
    [TestCase(typeof(AlcoGraphicsPipelineHandle))]
    [TestCase(typeof(AlcoComputePipelineHandle))]
    [TestCase(typeof(AlcoEncoderHandle))]
    [TestCase(typeof(AlcoCommandBufferHandle))]
    [TestCase(typeof(AlcoRenderPassHandle))]
    [TestCase(typeof(AlcoComputePassHandle))]
    [TestCase(typeof(AlcoBundleEncoderHandle))]
    [TestCase(typeof(AlcoRenderBundleHandle))]
    [TestCase(typeof(AlcoSurfaceHandle))]
    public void TypedHandlesHaveSinglePointerLayout(Type handleType)
    {
        FieldInfo[] fields = handleType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(fields, Has.Length.EqualTo(1));
        Assert.That(fields[0].FieldType, Is.EqualTo(typeof(nint)));
        Assert.That(fields[0].IsInitOnly, Is.True);
        Assert.That(Marshal.SizeOf(handleType), Is.EqualTo(IntPtr.Size));
        Assert.That(Marshal.OffsetOf(handleType, fields[0].Name), Is.EqualTo(IntPtr.Zero));
        Assert.That(handleType.StructLayoutAttribute!.Value, Is.EqualTo(LayoutKind.Sequential));
        Assert.That(handleType.GetProperty("IsNull")!.GetValue(Activator.CreateInstance(handleType)), Is.True);
    }

    /// <summary>Verifies descriptors preserve object kinds instead of using interchangeable handles.</summary>
    [Test]
    public void DescriptorResourcesUseTypedHandlesExceptTaggedBindings()
    {
        static void FieldHasType(Type descriptor, string field, Type expected) =>
            Assert.That(descriptor.GetField(field)!.FieldType, Is.EqualTo(expected), $"{descriptor.Name}.{field}");

        FieldHasType(typeof(AlcoBindGroupDesc), nameof(AlcoBindGroupDesc.Layout), typeof(AlcoBindGroupLayoutHandle));
        FieldHasType(typeof(AlcoGraphicsPipelineDesc), nameof(AlcoGraphicsPipelineDesc.BindGroupLayouts), typeof(AlcoBindGroupLayoutHandle*));
        FieldHasType(typeof(AlcoGraphicsPipelineDesc), nameof(AlcoGraphicsPipelineDesc.VertexModule), typeof(AlcoShaderModuleHandle));
        FieldHasType(typeof(AlcoGraphicsPipelineDesc), nameof(AlcoGraphicsPipelineDesc.FragmentModule), typeof(AlcoShaderModuleHandle));
        FieldHasType(typeof(AlcoComputePipelineDesc), nameof(AlcoComputePipelineDesc.BindGroupLayouts), typeof(AlcoBindGroupLayoutHandle*));
        FieldHasType(typeof(AlcoComputePipelineDesc), nameof(AlcoComputePipelineDesc.ComputeModule), typeof(AlcoShaderModuleHandle));
        FieldHasType(typeof(AlcoColorAttachment), nameof(AlcoColorAttachment.View), typeof(AlcoTextureViewHandle));
        FieldHasType(typeof(AlcoColorAttachment), nameof(AlcoColorAttachment.ResolveView), typeof(AlcoTextureViewHandle));
        FieldHasType(typeof(AlcoDepthStencilAttachment), nameof(AlcoDepthStencilAttachment.View), typeof(AlcoTextureViewHandle));
        FieldHasType(typeof(AlcoTimestampWrites), nameof(AlcoTimestampWrites.QuerySet), typeof(AlcoQuerySetHandle));
        FieldHasType(typeof(AlcoBindGroupEntry), nameof(AlcoBindGroupEntry.Resource), typeof(nint));
        FieldHasType(typeof(AlcoBindGroupEntry), nameof(AlcoBindGroupEntry.Kind), typeof(uint));
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
        AlcoDeviceHandle device = CreateNativeDevice();
        Assert.That(device.IsNull, Is.False);
        try
        {
            AlcoDeviceInfo info = default;
            AlcoGpuNative.DeviceGetInfo(device, ref info);
            Assert.That(info.Backend, Is.EqualTo(AlcoGpuAbi.BackendResolved.Vulkan)
                .Or.EqualTo(AlcoGpuAbi.BackendResolved.Dx12)
                .Or.EqualTo(AlcoGpuAbi.BackendResolved.Metal));
            Assert.That(info.Caps & AlcoGpuAbi.Caps.PassthroughShaders, Is.Not.Zero,
                "passthrough shaders expected on desktop adapters");
            if (OperatingSystem.IsMacOS())
            {
                Assert.That(info.Caps & AlcoGpuAbi.Caps.MetalLib, Is.Not.Zero,
                    "MetalLib passthrough expected on Apple platforms");
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

    /// <summary>Null pointers fail recoverably; successful calls preserve the latest failure on the calling thread.</summary>
    [Test]
    public void LastErrorRetainsLatestFailureAcrossSuccessAndNotReady()
    {
        AlcoDeviceHandle device = CreateNativeDevice();
        AlcoBufferHandle buffer = AlcoBufferHandle.Null;
        try
        {
            GraphicsException nullError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BufferDestroy(AlcoBufferHandle.Null))!;
            Assert.That(nullError.Message, Does.Contain("handle"));
            AlcoErrorInfo error = default;
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));
            string? nullMessage = AlcoGpuMarshal.BorrowedString(error.Message);
            Assert.That(nullMessage, Is.Not.Null.And.Not.Empty);
            byte* nullMessagePointer = error.Message;

            AlcoBufferDesc descriptor = new() { Size = 64, Usage = (uint)BufferUsage.CopyDst };
            AlcoGpuNative.BufferCreate(device, in descriptor, out buffer);
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));
            Assert.That((nint)error.Message, Is.EqualTo((nint)nullMessagePointer));
            Assert.That(AlcoGpuMarshal.BorrowedString(error.Message), Is.EqualTo(nullMessage));

            // This is a live buffer, but its usage does not permit mapping. Never probe a freed pointer.
            GraphicsException mapError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BufferMapRead(buffer, 0, 64))!;
            Assert.That(mapError.Message, Does.Contain("validation"));
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGpuAbi.Status.Validation));
            string? latestMessage = AlcoGpuMarshal.BorrowedString(error.Message);
            Assert.That(latestMessage, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(nullMessage));
            byte* latestPointer = error.Message;

            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoFalse, 0, &queueEmpty);
            AlcoDeviceMessage message = default;
            Assert.That(AlcoGpuNative.DevicePopMessage(device, ref message), Is.EqualTo(AlcoGpuAbi.Status.NotReady));
            AlcoBuildInfo buildInfo = default;
            AlcoGpuNative.BuildInfo(ref buildInfo);
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGpuAbi.Status.Validation));
            Assert.That((nint)error.Message, Is.EqualTo((nint)latestPointer));
            Assert.That(AlcoGpuMarshal.BorrowedString(error.Message), Is.EqualTo(latestMessage));
        }
        finally
        {
            if (!buffer.IsNull)
            {
                AlcoGpuNative.BufferDestroy(buffer);
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Repeatedly allocates independent live wrappers and validates every real GPU buffer copy.</summary>
    [Test]
    public void RepeatedBufferAllocationAndDestructionPreservesIndependentCopies()
    {
        const int count = 32;
        const int rounds = 32;
        const int size = 64;
        AlcoDeviceHandle device = CreateNativeDevice();
        var sources = new AlcoBufferHandle[count];
        var destinations = new AlcoBufferHandle[count];
        AlcoEncoderHandle encoder = AlcoEncoderHandle.Null;
        AlcoCommandBufferHandle commands = AlcoCommandBufferHandle.Null;
        AlcoBufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.CopySrc | BufferUsage.CopyDst) };
        AlcoBufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
        byte* written = stackalloc byte[size];
        try
        {
            for (int round = 0; round < rounds; round++)
            {
                // Compare only simultaneously live identities. Allocators may reuse any released address.
                var identities = new HashSet<nint>();
                AlcoGpuNative.EncoderCreate(device, null, out encoder);
                for (int i = 0; i < count; i++)
                {
                    AlcoGpuNative.BufferCreate(device, in sourceDescriptor, out sources[i]);
                    AlcoGpuNative.BufferCreate(device, in destinationDescriptor, out destinations[i]);
                    Assert.That(sources[i].IsNull, Is.False);
                    Assert.That(destinations[i].IsNull, Is.False);
                    Assert.That(identities.Add(sources[i].Value), Is.True, "two live buffers share an identity");
                    Assert.That(identities.Add(destinations[i].Value), Is.True, "two live buffers share an identity");
                    for (int element = 0; element < size; element++)
                    {
                        written[element] = (byte)(round * 11 + i * 7 + element);
                    }
                    AlcoGpuNative.QueueWriteBuffer(device, sources[i], 0, written, size);
                    AlcoGpuNative.CopyBufferToBuffer(encoder, sources[i], 0, destinations[i], 0, size);
                }
                AlcoEncoderHandle finishedEncoder = encoder;
                encoder = AlcoEncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(finishedEncoder, out commands);
                AlcoCommandBufferHandle submitted = commands;
                commands = AlcoCommandBufferHandle.Null;
                ulong submissionIndex = 0;
                AlcoGpuNative.QueueSubmit(device, submitted, &submissionIndex);
                for (int i = 0; i < count; i++)
                {
                    AlcoGpuNative.BufferMapRead(destinations[i], 0, size);
                }
                uint queueEmpty = 0;
                AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoTrue, submissionIndex, &queueEmpty);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(AlcoGpuNative.BufferMapPoll(destinations[i]), Is.EqualTo(AlcoGpuAbi.Status.Ok));
                    void* mapped = null;
                    AlcoGpuNative.BufferGetMappedRange(destinations[i], 0, size, &mapped);
                    Assert.That((nint)mapped, Is.Not.EqualTo(nint.Zero));
                    try
                    {
                        for (int element = 0; element < size; element++)
                        {
                            Assert.That(((byte*)mapped)[element], Is.EqualTo((byte)(round * 11 + i * 7 + element)),
                                $"Round {round}, buffer {i}, byte {element}");
                        }
                    }
                    finally
                    {
                        AlcoGpuNative.BufferUnmap(destinations[i]);
                    }
                    AlcoBufferHandle destination = destinations[i];
                    destinations[i] = AlcoBufferHandle.Null;
                    AlcoGpuNative.BufferDestroy(destination);
                    AlcoBufferHandle source = sources[i];
                    sources[i] = AlcoBufferHandle.Null;
                    AlcoGpuNative.BufferDestroy(source);
                }
            }
        }
        finally
        {
            if (!commands.IsNull)
            {
                AlcoGpuNative.CommandBufferDestroy(commands);
            }
            if (!encoder.IsNull)
            {
                AlcoGpuNative.EncoderDestroy(encoder);
            }
            for (int i = 0; i < count; i++)
            {
                if (!destinations[i].IsNull)
                {
                    AlcoGpuNative.BufferDestroy(destinations[i]);
                }
                if (!sources[i].IsNull)
                {
                    AlcoGpuNative.BufferDestroy(sources[i]);
                }
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Submits independently finished command buffers as one array submission and validates every GPU copy.</summary>
    [Test]
    public void BatchSubmitExecutesEveryCommandBufferUnderOneSubmissionIndex()
    {
        const int count = 4;
        const int size = 64;
        AlcoDeviceHandle device = CreateNativeDevice();
        var sources = new AlcoBufferHandle[count];
        var destinations = new AlcoBufferHandle[count];
        var encoders = new AlcoEncoderHandle[count];
        var commands = new AlcoCommandBufferHandle[count];
        byte* written = stackalloc byte[size];
        try
        {
            for (int i = 0; i < count; i++)
            {
                AlcoBufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.CopySrc | BufferUsage.CopyDst) };
                AlcoBufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
                AlcoGpuNative.BufferCreate(device, in sourceDescriptor, out sources[i]);
                AlcoGpuNative.BufferCreate(device, in destinationDescriptor, out destinations[i]);
                AlcoGpuNative.EncoderCreate(device, null, out encoders[i]);
                for (int element = 0; element < size; element++)
                {
                    written[element] = (byte)(i * 13 + element);
                }
                AlcoGpuNative.QueueWriteBuffer(device, sources[i], 0, written, size);
                AlcoGpuNative.CopyBufferToBuffer(encoders[i], sources[i], 0, destinations[i], 0, size);
            }
            for (int i = 0; i < count; i++)
            {
                AlcoEncoderHandle encoder = encoders[i];
                encoders[i] = AlcoEncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(encoder, out commands[i]);
            }
            ulong submissionIndex = 0;
            fixed (AlcoCommandBufferHandle* submitted = commands)
            {
                AlcoGpuNative.QueueSubmitBatch(device, submitted, count, &submissionIndex);
            }
            for (int i = 0; i < count; i++)
            {
                // Submit consumed every wrapper; only the buffers remain to clean up.
                commands[i] = AlcoCommandBufferHandle.Null;
                AlcoGpuNative.BufferMapRead(destinations[i], 0, size);
            }
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoTrue, submissionIndex, &queueEmpty);
            for (int i = 0; i < count; i++)
            {
                Assert.That(AlcoGpuNative.BufferMapPoll(destinations[i]), Is.EqualTo(AlcoGpuAbi.Status.Ok));
                void* mapped = null;
                AlcoGpuNative.BufferGetMappedRange(destinations[i], 0, size, &mapped);
                Assert.That((nint)mapped, Is.Not.EqualTo(nint.Zero));
                try
                {
                    for (int element = 0; element < size; element++)
                    {
                        Assert.That(((byte*)mapped)[element], Is.EqualTo((byte)(i * 13 + element)),
                            $"Buffer {i}, byte {element}");
                    }
                }
                finally
                {
                    AlcoGpuNative.BufferUnmap(destinations[i]);
                }
            }
        }
        finally
        {
            for (int i = 0; i < count; i++)
            {
                if (!commands[i].IsNull)
                {
                    AlcoGpuNative.CommandBufferDestroy(commands[i]);
                }
                if (!encoders[i].IsNull)
                {
                    AlcoGpuNative.EncoderDestroy(encoders[i]);
                }
                if (!destinations[i].IsNull)
                {
                    AlcoGpuNative.BufferDestroy(destinations[i]);
                }
                if (!sources[i].IsNull)
                {
                    AlcoGpuNative.BufferDestroy(sources[i]);
                }
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Writes through a write map, copies to a read-mapped buffer and verifies every byte.</summary>
    [Test]
    public void WriteMappingRoundTripsThroughReadMapping()
    {
        const ulong size = 64;
        AlcoDeviceHandle device = CreateNativeDevice();
        AlcoBufferHandle source = AlcoBufferHandle.Null;
        AlcoBufferHandle destination = AlcoBufferHandle.Null;
        AlcoEncoderHandle encoder = AlcoEncoderHandle.Null;
        AlcoCommandBufferHandle command = AlcoCommandBufferHandle.Null;
        AlcoBufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapWrite | BufferUsage.CopySrc) };
        AlcoBufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
        try
        {
            AlcoGpuNative.BufferCreate(device, in sourceDescriptor, out source);
            AlcoGpuNative.BufferCreate(device, in destinationDescriptor, out destination);
            AlcoGpuNative.BufferMapWrite(source, 0, size);
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoTrue, ulong.MaxValue, &queueEmpty);
            Assert.That(AlcoGpuNative.BufferMapPoll(source), Is.EqualTo(AlcoGpuAbi.Status.Ok));
            void* mapped = null;
            AlcoGpuNative.BufferGetMappedRange(source, 0, size, &mapped);
            Assert.That((nint)mapped, Is.Not.EqualTo(nint.Zero));
            for (int element = 0; element < (int)size; element++)
            {
                ((byte*)mapped)[element] = (byte)(element * 3 % 251);
            }
            AlcoGpuNative.BufferUnmap(source);

            AlcoGpuNative.EncoderCreate(device, null, out encoder);
            AlcoGpuNative.CopyBufferToBuffer(encoder, source, 0, destination, 0, size);
            AlcoEncoderHandle finishedEncoder = encoder;
            encoder = AlcoEncoderHandle.Null;
            AlcoGpuNative.EncoderFinish(finishedEncoder, out command);
            ulong submissionIndex = 0;
            AlcoGpuNative.QueueSubmit(device, command, &submissionIndex);
            command = AlcoCommandBufferHandle.Null;
            uint submitQueueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoTrue, submissionIndex, &submitQueueEmpty);

            AlcoGpuNative.BufferMapRead(destination, 0, size);
            uint readQueueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoTrue, ulong.MaxValue, &readQueueEmpty);
            Assert.That(AlcoGpuNative.BufferMapPoll(destination), Is.EqualTo(AlcoGpuAbi.Status.Ok));
            void* read = null;
            AlcoGpuNative.BufferGetMappedRange(destination, 0, size, &read);
            Assert.That((nint)read, Is.Not.EqualTo(nint.Zero));
            try
            {
                for (int element = 0; element < (int)size; element++)
                {
                    Assert.That(((byte*)read)[element], Is.EqualTo((byte)(element * 3 % 251)),
                        $"Element {element}");
                }
            }
            finally
            {
                AlcoGpuNative.BufferUnmap(destination);
            }
        }
        finally
        {
            if (!command.IsNull)
            {
                AlcoGpuNative.CommandBufferDestroy(command);
            }
            if (!encoder.IsNull)
            {
                AlcoGpuNative.EncoderDestroy(encoder);
            }
            if (!source.IsNull)
            {
                AlcoGpuNative.BufferDestroy(source);
            }
            if (!destination.IsNull)
            {
                AlcoGpuNative.BufferDestroy(destination);
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
        AlcoDeviceHandle device = CreateNativeDevice();
        AlcoBindGroupLayoutHandle layout = AlcoBindGroupLayoutHandle.Null;
        AlcoBindGroupHandle group = AlcoBindGroupHandle.Null;
        AlcoBindGroupLayoutEntry layoutEntry = default;
        AlcoBindGroupEntry groupEntry = default;
        try
        {
            AlcoBindGroupLayoutDesc layoutDescriptor = new() { Entries = useNullEntries ? null : &layoutEntry };
            AlcoGpuNative.BindGroupLayoutCreate(device, in layoutDescriptor, out layout);
            AlcoBindGroupDesc groupDescriptor = new() { Layout = layout, Entries = useNullEntries ? null : &groupEntry };
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
                AlcoGpuNative.BindGroupDestroy(group);
            }
            if (!layout.IsNull)
            {
                AlcoGpuNative.BindGroupLayoutDestroy(layout);
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
        AlcoDeviceHandle device = CreateNativeDevice();
        AlcoTextureHandle texture = AlcoTextureHandle.Null;
        AlcoTextureViewHandle view = AlcoTextureViewHandle.Null;
        AlcoEncoderHandle encoder = AlcoEncoderHandle.Null;
        AlcoRenderPassHandle pass = AlcoRenderPassHandle.Null;
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
            AlcoGpuNative.TextureCreateView(texture, null, out view);
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
            AlcoGpuNative.RenderPassBegin(encoder, in descriptor, out pass);
            AlcoRenderPassHandle endedPass = pass;
            pass = AlcoRenderPassHandle.Null;
            AlcoGpuNative.RenderPassEnd(endedPass);
            AlcoEncoderHandle finishedEncoder = encoder;
            encoder = AlcoEncoderHandle.Null;
            AlcoCommandBufferHandle buffer = AlcoCommandBufferHandle.Null;
            GraphicsException finishError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.EncoderFinish(finishedEncoder, out buffer))!;
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
                AlcoGpuNative.RenderPassRelease(pass);
            }
            if (!encoder.IsNull)
            {
                AlcoGpuNative.EncoderDestroy(encoder);
            }
            if (!view.IsNull)
            {
                AlcoGpuNative.TextureViewDestroy(view);
            }
            if (!texture.IsNull)
            {
                AlcoGpuNative.TextureDestroy(texture);
            }
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    private static AlcoDeviceHandle CreateNativeDevice()
    {
        AlcoDeviceDesc descriptor = new()
        {
            Backend = AlcoGpuAbi.BackendRequest.Auto,
            Debug = AlcoGpuAbi.AlcoTrue,
            PushConstantsSize = 128,
        };
        AlcoGpuNative.DeviceCreate(in descriptor, out AlcoDeviceHandle device);
        return device;
    }

    /// <summary>Verifies nonblocking polls and repeated empty-message polls remain exception-free.</summary>
    [Test]
    public void PollAndEmptyMessageQueueAreSafe()
    {
        AlcoDeviceHandle device = CreateNativeDevice();
        try
        {
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoFalse, 0, &queueEmpty);
            AlcoDeviceMessage message = default;
            for (int poll = 0; poll < 2; poll++)
            {
                Assert.That(AlcoGpuNative.DevicePopMessage(device, ref message), Is.EqualTo(AlcoGpuAbi.Status.NotReady));
            }
        }
        finally
        {
            AlcoGpuNative.DeviceDestroy(device);
        }
    }

    /// <summary>Verifies documented log levels are accepted and unknown levels throw through the error callback.</summary>
    [Test]
    public void LogLevelAcceptsKnownValuesAndRejectsUnknownValues()
    {
        try
        {
            foreach (uint level in new[]
                     {
                         AlcoGpuAbi.LogLevel.Off, AlcoGpuAbi.LogLevel.Error, AlcoGpuAbi.LogLevel.Warn,
                         AlcoGpuAbi.LogLevel.Info, AlcoGpuAbi.LogLevel.Debug, AlcoGpuAbi.LogLevel.Trace,
                     })
            {
                Assert.That(AlcoGpuNative.SetLogLevel(level), Is.EqualTo(AlcoGpuAbi.Status.Ok));
            }
            GraphicsException error = Assert.Throws<GraphicsException>(() => AlcoGpuNative.SetLogLevel(99))!;
            Assert.That(error.Message, Does.Contain("log level"));
        }
        finally
        {
            AlcoGpuNative.SetLogLevel(AlcoGpuAbi.LogLevel.Warn);
        }
    }

    /// <summary>Verifies repeated native log callback registrations keep the installed forwarder in place.</summary>
    [Test]
    public void LogCallbackRegistrationIsIdempotent()
    {
        Assert.That(AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null), Is.EqualTo(AlcoGpuAbi.Status.Ok));
        Assert.That(AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null), Is.EqualTo(AlcoGpuAbi.Status.Ok));
    }
}
