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
public unsafe class AlcoGPUTests
{
    /// <summary>Verifies that both sides implement the ABI 3 version handshake.</summary>
    [Test]
    public void AbiVersionMatches()
    {
        Assert.That(AlcoGPU.AbiMajor, Is.EqualTo(3));
        Assert.That(AlcoGPU.AbiMinor, Is.EqualTo(0));
        uint version = AlcoGpuNative.AbiVersion();
        Assert.That(version >> 16, Is.EqualTo(AlcoGPU.AbiMajor));
        Assert.That(version & 0xFFFF, Is.GreaterThanOrEqualTo(AlcoGPU.AbiMinor));
    }

    /// <summary>Verifies every typed handle contains exactly one native-sized pointer.</summary>
    /// <param name="handleType">The managed handle type whose native layout is checked.</param>
    [TestCase(typeof(AlcoGPU.DeviceHandle))]
    [TestCase(typeof(AlcoGPU.BufferHandle))]
    [TestCase(typeof(AlcoGPU.TextureHandle))]
    [TestCase(typeof(AlcoGPU.TextureViewHandle))]
    [TestCase(typeof(AlcoGPU.SamplerHandle))]
    [TestCase(typeof(AlcoGPU.ShaderModuleHandle))]
    [TestCase(typeof(AlcoGPU.BindGroupLayoutHandle))]
    [TestCase(typeof(AlcoGPU.BindGroupHandle))]
    [TestCase(typeof(AlcoGPU.QuerySetHandle))]
    [TestCase(typeof(AlcoGPU.GraphicsPipelineHandle))]
    [TestCase(typeof(AlcoGPU.ComputePipelineHandle))]
    [TestCase(typeof(AlcoGPU.EncoderHandle))]
    [TestCase(typeof(AlcoGPU.CommandBufferHandle))]
    [TestCase(typeof(AlcoGPU.RenderPassHandle))]
    [TestCase(typeof(AlcoGPU.ComputePassHandle))]
    [TestCase(typeof(AlcoGPU.BundleEncoderHandle))]
    [TestCase(typeof(AlcoGPU.RenderBundleHandle))]
    [TestCase(typeof(AlcoGPU.SurfaceHandle))]
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

        FieldHasType(typeof(AlcoGPU.BindGroupDesc), nameof(AlcoGPU.BindGroupDesc.Layout), typeof(AlcoGPU.BindGroupLayoutHandle));
        FieldHasType(typeof(AlcoGPU.GraphicsPipelineDesc), nameof(AlcoGPU.GraphicsPipelineDesc.BindGroupLayouts), typeof(AlcoGPU.BindGroupLayoutHandle*));
        FieldHasType(typeof(AlcoGPU.GraphicsPipelineDesc), nameof(AlcoGPU.GraphicsPipelineDesc.VertexModule), typeof(AlcoGPU.ShaderModuleHandle));
        FieldHasType(typeof(AlcoGPU.GraphicsPipelineDesc), nameof(AlcoGPU.GraphicsPipelineDesc.FragmentModule), typeof(AlcoGPU.ShaderModuleHandle));
        FieldHasType(typeof(AlcoGPU.ComputePipelineDesc), nameof(AlcoGPU.ComputePipelineDesc.BindGroupLayouts), typeof(AlcoGPU.BindGroupLayoutHandle*));
        FieldHasType(typeof(AlcoGPU.ComputePipelineDesc), nameof(AlcoGPU.ComputePipelineDesc.ComputeModule), typeof(AlcoGPU.ShaderModuleHandle));
        FieldHasType(typeof(AlcoGPU.ColorAttachment), nameof(AlcoGPU.ColorAttachment.View), typeof(AlcoGPU.TextureViewHandle));
        FieldHasType(typeof(AlcoGPU.ColorAttachment), nameof(AlcoGPU.ColorAttachment.ResolveView), typeof(AlcoGPU.TextureViewHandle));
        FieldHasType(typeof(AlcoGPU.DepthStencilAttachment), nameof(AlcoGPU.DepthStencilAttachment.View), typeof(AlcoGPU.TextureViewHandle));
        FieldHasType(typeof(AlcoGPU.TimestampWrites), nameof(AlcoGPU.TimestampWrites.QuerySet), typeof(AlcoGPU.QuerySetHandle));
        FieldHasType(typeof(AlcoGPU.BindGroupEntry), nameof(AlcoGPU.BindGroupEntry.Resource), typeof(nint));
        FieldHasType(typeof(AlcoGPU.BindGroupEntry), nameof(AlcoGPU.BindGroupEntry.Kind), typeof(uint));
    }

    /// <summary>Verifies native build information identifies the pinned wgpu version.</summary>
    [Test]
    public void BuildInfoCarriesWgpuVersion()
    {
        AlcoGPU.BuildInfo info = default;
        AlcoGpuNative.BuildInfo(ref info);
        Assert.That(info.WgpuVersion >> 16, Is.EqualTo(30));
        string? build = AlcoGpuMarshal.BorrowedString(info.BuildId);
        Assert.That(build, Is.Not.Null.And.Contains("alco-gpu"));
    }

    /// <summary>Creates a device and validates capabilities across supported desktop backends.</summary>
    [Test]
    public void DeviceCreateGetInfoDestroyRoundTrip()
    {
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        Assert.That(device.IsNull, Is.False);
        try
        {
            AlcoGPU.DeviceInfo info = default;
            AlcoGpuNative.DeviceGetInfo(device, ref info);
            Assert.That(info.Backend, Is.EqualTo(AlcoGPU.BackendResolved.Vulkan)
                .Or.EqualTo(AlcoGPU.BackendResolved.Dx12)
                .Or.EqualTo(AlcoGPU.BackendResolved.Metal));
            Assert.That(info.Capabilities & AlcoGPU.Capabilities.PassthroughShaders, Is.Not.Zero,
                "passthrough shaders expected on desktop adapters");
            if (OperatingSystem.IsMacOS())
            {
                Assert.That(info.Capabilities & AlcoGPU.Capabilities.MetalLib, Is.Not.Zero,
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
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        AlcoGPU.BufferHandle buffer = AlcoGPU.BufferHandle.Null;
        try
        {
            GraphicsException nullError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BufferDestroy(AlcoGPU.BufferHandle.Null))!;
            Assert.That(nullError.Message, Does.Contain("handle"));
            AlcoGPU.ErrorInfo error = default;
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGPU.Status.InvalidHandle));
            string? nullMessage = AlcoGpuMarshal.BorrowedString(error.Message);
            Assert.That(nullMessage, Is.Not.Null.And.Not.Empty);
            byte* nullMessagePointer = error.Message;

            AlcoGPU.BufferDesc descriptor = new() { Size = 64, Usage = (uint)BufferUsage.CopyDst };
            AlcoGpuNative.BufferCreate(device, in descriptor, out buffer);
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGPU.Status.InvalidHandle));
            Assert.That((nint)error.Message, Is.EqualTo((nint)nullMessagePointer));
            Assert.That(AlcoGpuMarshal.BorrowedString(error.Message), Is.EqualTo(nullMessage));

            // This is a live buffer, but its usage does not permit mapping. Never probe a freed pointer.
            GraphicsException mapError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BufferMapRead(buffer, 0, 64))!;
            Assert.That(mapError.Message, Does.Contain("validation"));
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGPU.Status.Validation));
            string? latestMessage = AlcoGpuMarshal.BorrowedString(error.Message);
            Assert.That(latestMessage, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(nullMessage));
            byte* latestPointer = error.Message;

            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGPU.False, 0, &queueEmpty);
            AlcoGPU.DeviceMessage message = default;
            Assert.That(AlcoGpuNative.DevicePopMessage(device, ref message), Is.EqualTo(AlcoGPU.Status.NotReady));
            AlcoGPU.BuildInfo buildInfo = default;
            AlcoGpuNative.BuildInfo(ref buildInfo);
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGPU.Status.Validation));
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
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        var sources = new AlcoGPU.BufferHandle[count];
        var destinations = new AlcoGPU.BufferHandle[count];
        AlcoGPU.EncoderHandle encoder = AlcoGPU.EncoderHandle.Null;
        AlcoGPU.CommandBufferHandle commands = AlcoGPU.CommandBufferHandle.Null;
        AlcoGPU.BufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.CopySrc | BufferUsage.CopyDst) };
        AlcoGPU.BufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
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
                AlcoGPU.EncoderHandle finishedEncoder = encoder;
                encoder = AlcoGPU.EncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(finishedEncoder, out commands);
                AlcoGPU.CommandBufferHandle submitted = commands;
                commands = AlcoGPU.CommandBufferHandle.Null;
                ulong submissionIndex = 0;
                AlcoGpuNative.QueueSubmit(device, submitted, &submissionIndex);
                for (int i = 0; i < count; i++)
                {
                    AlcoGpuNative.BufferMapRead(destinations[i], 0, size);
                }
                uint queueEmpty = 0;
                AlcoGpuNative.DevicePoll(device, AlcoGPU.True, submissionIndex, &queueEmpty);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(AlcoGpuNative.BufferMapPoll(destinations[i]), Is.EqualTo(AlcoGPU.Status.Ok));
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
                    AlcoGPU.BufferHandle destination = destinations[i];
                    destinations[i] = AlcoGPU.BufferHandle.Null;
                    AlcoGpuNative.BufferDestroy(destination);
                    AlcoGPU.BufferHandle source = sources[i];
                    sources[i] = AlcoGPU.BufferHandle.Null;
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
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        var sources = new AlcoGPU.BufferHandle[count];
        var destinations = new AlcoGPU.BufferHandle[count];
        var encoders = new AlcoGPU.EncoderHandle[count];
        var commands = new AlcoGPU.CommandBufferHandle[count];
        byte* written = stackalloc byte[size];
        try
        {
            for (int i = 0; i < count; i++)
            {
                AlcoGPU.BufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.CopySrc | BufferUsage.CopyDst) };
                AlcoGPU.BufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
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
                AlcoGPU.EncoderHandle encoder = encoders[i];
                encoders[i] = AlcoGPU.EncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(encoder, out commands[i]);
            }
            ulong submissionIndex = 0;
            fixed (AlcoGPU.CommandBufferHandle* submitted = commands)
            {
                AlcoGpuNative.QueueSubmitBatch(device, submitted, count, &submissionIndex);
            }
            for (int i = 0; i < count; i++)
            {
                // Submit consumed every wrapper; only the buffers remain to clean up.
                commands[i] = AlcoGPU.CommandBufferHandle.Null;
                AlcoGpuNative.BufferMapRead(destinations[i], 0, size);
            }
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGPU.True, submissionIndex, &queueEmpty);
            for (int i = 0; i < count; i++)
            {
                Assert.That(AlcoGpuNative.BufferMapPoll(destinations[i]), Is.EqualTo(AlcoGPU.Status.Ok));
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
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        AlcoGPU.BufferHandle source = AlcoGPU.BufferHandle.Null;
        AlcoGPU.BufferHandle destination = AlcoGPU.BufferHandle.Null;
        AlcoGPU.EncoderHandle encoder = AlcoGPU.EncoderHandle.Null;
        AlcoGPU.CommandBufferHandle command = AlcoGPU.CommandBufferHandle.Null;
        AlcoGPU.BufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapWrite | BufferUsage.CopySrc) };
        AlcoGPU.BufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
        try
        {
            AlcoGpuNative.BufferCreate(device, in sourceDescriptor, out source);
            AlcoGpuNative.BufferCreate(device, in destinationDescriptor, out destination);
            AlcoGpuNative.BufferMapWrite(source, 0, size);
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGPU.True, ulong.MaxValue, &queueEmpty);
            Assert.That(AlcoGpuNative.BufferMapPoll(source), Is.EqualTo(AlcoGPU.Status.Ok));
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
            AlcoGPU.EncoderHandle finishedEncoder = encoder;
            encoder = AlcoGPU.EncoderHandle.Null;
            AlcoGpuNative.EncoderFinish(finishedEncoder, out command);
            ulong submissionIndex = 0;
            AlcoGpuNative.QueueSubmit(device, command, &submissionIndex);
            command = AlcoGPU.CommandBufferHandle.Null;
            uint submitQueueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGPU.True, submissionIndex, &submitQueueEmpty);

            AlcoGpuNative.BufferMapRead(destination, 0, size);
            uint readQueueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGPU.True, ulong.MaxValue, &readQueueEmpty);
            Assert.That(AlcoGpuNative.BufferMapPoll(destination), Is.EqualTo(AlcoGPU.Status.Ok));
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
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        AlcoGPU.BindGroupLayoutHandle layout = AlcoGPU.BindGroupLayoutHandle.Null;
        AlcoGPU.BindGroupHandle group = AlcoGPU.BindGroupHandle.Null;
        AlcoGPU.BindGroupLayoutEntry layoutEntry = default;
        AlcoGPU.BindGroupEntry groupEntry = default;
        try
        {
            AlcoGPU.BindGroupLayoutDesc layoutDescriptor = new() { Entries = useNullEntries ? null : &layoutEntry };
            AlcoGpuNative.BindGroupLayoutCreate(device, in layoutDescriptor, out layout);
            AlcoGPU.BindGroupDesc groupDescriptor = new() { Layout = layout, Entries = useNullEntries ? null : &groupEntry };
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
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        AlcoGPU.TextureHandle texture = AlcoGPU.TextureHandle.Null;
        AlcoGPU.TextureViewHandle view = AlcoGPU.TextureViewHandle.Null;
        AlcoGPU.EncoderHandle encoder = AlcoGPU.EncoderHandle.Null;
        AlcoGPU.RenderPassHandle pass = AlcoGPU.RenderPassHandle.Null;
        try
        {
            AlcoGPU.TextureDesc textureDescriptor = new()
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
            AlcoGPU.DepthStencilAttachment depth = new()
            {
                View = view,
                DepthLoadOp = 1,
                DepthStoreOp = 0,
                DepthClear = invalidStencil ? 1f : 2f,
                StencilLoadOp = invalidStencil ? 0 : AlcoGPU.None,
                StencilStoreOp = invalidStencil ? 0 : AlcoGPU.None,
            };
            AlcoGPU.RenderPassDesc descriptor = new() { DepthStencil = &depth };
            AlcoGpuNative.RenderPassBegin(encoder, in descriptor, out pass);
            AlcoGPU.RenderPassHandle endedPass = pass;
            pass = AlcoGPU.RenderPassHandle.Null;
            AlcoGpuNative.RenderPassEnd(endedPass);
            AlcoGPU.EncoderHandle finishedEncoder = encoder;
            encoder = AlcoGPU.EncoderHandle.Null;
            AlcoGPU.CommandBufferHandle buffer = AlcoGPU.CommandBufferHandle.Null;
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

    private static AlcoGPU.DeviceHandle CreateNativeDevice()
    {
        AlcoGPU.DeviceDesc descriptor = new()
        {
            Backend = AlcoGPU.BackendRequest.Auto,
            Debug = AlcoGPU.True,
            PushConstantsSize = 128,
        };
        AlcoGpuNative.DeviceCreate(in descriptor, out AlcoGPU.DeviceHandle device);
        return device;
    }

    /// <summary>Verifies nonblocking polls and repeated empty-message polls remain exception-free.</summary>
    [Test]
    public void PollAndEmptyMessageQueueAreSafe()
    {
        AlcoGPU.DeviceHandle device = CreateNativeDevice();
        try
        {
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGPU.False, 0, &queueEmpty);
            AlcoGPU.DeviceMessage message = default;
            for (int poll = 0; poll < 2; poll++)
            {
                Assert.That(AlcoGpuNative.DevicePopMessage(device, ref message), Is.EqualTo(AlcoGPU.Status.NotReady));
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
                         AlcoGPU.LogLevel.Off, AlcoGPU.LogLevel.Error, AlcoGPU.LogLevel.Warn,
                         AlcoGPU.LogLevel.Info, AlcoGPU.LogLevel.Debug, AlcoGPU.LogLevel.Trace,
                     })
            {
                Assert.That(AlcoGpuNative.SetLogLevel(level), Is.EqualTo(AlcoGPU.Status.Ok));
            }
            GraphicsException error = Assert.Throws<GraphicsException>(() => AlcoGpuNative.SetLogLevel(99))!;
            Assert.That(error.Message, Does.Contain("log level"));
        }
        finally
        {
            AlcoGpuNative.SetLogLevel(AlcoGPU.LogLevel.Warn);
        }
    }

    /// <summary>Verifies repeated native log callback registrations keep the installed forwarder in place.</summary>
    [Test]
    public void LogCallbackRegistrationIsIdempotent()
    {
        Assert.That(AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null), Is.EqualTo(AlcoGPU.Status.Ok));
        Assert.That(AlcoGpuNative.SetLogCallback(&AlcoGpuMarshal.OnNativeLog, null), Is.EqualTo(AlcoGPU.Status.Ok));
    }
}
