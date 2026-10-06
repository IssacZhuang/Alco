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
    /// <summary>Verifies that both sides implement the ABI 3 version handshake.</summary>
    [Test]
    public void AbiVersionMatches()
    {
        Assert.That(AlcoGpuAbi.AbiMajor, Is.EqualTo(3));
        Assert.That(AlcoGpuAbi.AbiMinor, Is.EqualTo(0));
        uint version = AlcoGpuNative.AbiVersion();
        Assert.That(version >> 16, Is.EqualTo(AlcoGpuAbi.AbiMajor));
        Assert.That(version & 0xFFFF, Is.GreaterThanOrEqualTo(AlcoGpuAbi.AbiMinor));
    }

    /// <summary>Verifies every typed handle contains exactly one native-sized pointer.</summary>
    /// <param name="handleType">The managed handle type whose native layout is checked.</param>
    [TestCase(typeof(AlcoGpuAbi.DeviceHandle))]
    [TestCase(typeof(AlcoGpuAbi.BufferHandle))]
    [TestCase(typeof(AlcoGpuAbi.TextureHandle))]
    [TestCase(typeof(AlcoGpuAbi.TextureViewHandle))]
    [TestCase(typeof(AlcoGpuAbi.SamplerHandle))]
    [TestCase(typeof(AlcoGpuAbi.ShaderModuleHandle))]
    [TestCase(typeof(AlcoGpuAbi.BindGroupLayoutHandle))]
    [TestCase(typeof(AlcoGpuAbi.BindGroupHandle))]
    [TestCase(typeof(AlcoGpuAbi.QuerySetHandle))]
    [TestCase(typeof(AlcoGpuAbi.GraphicsPipelineHandle))]
    [TestCase(typeof(AlcoGpuAbi.ComputePipelineHandle))]
    [TestCase(typeof(AlcoGpuAbi.EncoderHandle))]
    [TestCase(typeof(AlcoGpuAbi.CommandBufferHandle))]
    [TestCase(typeof(AlcoGpuAbi.RenderPassHandle))]
    [TestCase(typeof(AlcoGpuAbi.ComputePassHandle))]
    [TestCase(typeof(AlcoGpuAbi.BundleEncoderHandle))]
    [TestCase(typeof(AlcoGpuAbi.RenderBundleHandle))]
    [TestCase(typeof(AlcoGpuAbi.SurfaceHandle))]
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

        FieldHasType(typeof(AlcoGpuAbi.BindGroupDesc), nameof(AlcoGpuAbi.BindGroupDesc.Layout), typeof(AlcoGpuAbi.BindGroupLayoutHandle));
        FieldHasType(typeof(AlcoGpuAbi.GraphicsPipelineDesc), nameof(AlcoGpuAbi.GraphicsPipelineDesc.BindGroupLayouts), typeof(AlcoGpuAbi.BindGroupLayoutHandle*));
        FieldHasType(typeof(AlcoGpuAbi.GraphicsPipelineDesc), nameof(AlcoGpuAbi.GraphicsPipelineDesc.VertexModule), typeof(AlcoGpuAbi.ShaderModuleHandle));
        FieldHasType(typeof(AlcoGpuAbi.GraphicsPipelineDesc), nameof(AlcoGpuAbi.GraphicsPipelineDesc.FragmentModule), typeof(AlcoGpuAbi.ShaderModuleHandle));
        FieldHasType(typeof(AlcoGpuAbi.ComputePipelineDesc), nameof(AlcoGpuAbi.ComputePipelineDesc.BindGroupLayouts), typeof(AlcoGpuAbi.BindGroupLayoutHandle*));
        FieldHasType(typeof(AlcoGpuAbi.ComputePipelineDesc), nameof(AlcoGpuAbi.ComputePipelineDesc.ComputeModule), typeof(AlcoGpuAbi.ShaderModuleHandle));
        FieldHasType(typeof(AlcoGpuAbi.ColorAttachment), nameof(AlcoGpuAbi.ColorAttachment.View), typeof(AlcoGpuAbi.TextureViewHandle));
        FieldHasType(typeof(AlcoGpuAbi.ColorAttachment), nameof(AlcoGpuAbi.ColorAttachment.ResolveView), typeof(AlcoGpuAbi.TextureViewHandle));
        FieldHasType(typeof(AlcoGpuAbi.DepthStencilAttachment), nameof(AlcoGpuAbi.DepthStencilAttachment.View), typeof(AlcoGpuAbi.TextureViewHandle));
        FieldHasType(typeof(AlcoGpuAbi.TimestampWrites), nameof(AlcoGpuAbi.TimestampWrites.QuerySet), typeof(AlcoGpuAbi.QuerySetHandle));
        FieldHasType(typeof(AlcoGpuAbi.BindGroupEntry), nameof(AlcoGpuAbi.BindGroupEntry.Resource), typeof(nint));
        FieldHasType(typeof(AlcoGpuAbi.BindGroupEntry), nameof(AlcoGpuAbi.BindGroupEntry.Kind), typeof(uint));
    }

    /// <summary>Verifies native build information identifies the pinned wgpu version.</summary>
    [Test]
    public void BuildInfoCarriesWgpuVersion()
    {
        AlcoGpuAbi.BuildInfo info = default;
        AlcoGpuNative.BuildInfo(ref info);
        Assert.That(info.WgpuVersion >> 16, Is.EqualTo(30));
        string? build = AlcoGpuMarshal.BorrowedString(info.BuildId);
        Assert.That(build, Is.Not.Null.And.Contains("alco-gpu"));
    }

    /// <summary>Creates a device and validates capabilities across supported desktop backends.</summary>
    [Test]
    public void DeviceCreateGetInfoDestroyRoundTrip()
    {
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        Assert.That(device.IsNull, Is.False);
        try
        {
            AlcoGpuAbi.DeviceInfo info = default;
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
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        AlcoGpuAbi.BufferHandle buffer = AlcoGpuAbi.BufferHandle.Null;
        try
        {
            GraphicsException nullError = Assert.Throws<GraphicsException>(
                () => AlcoGpuNative.BufferDestroy(AlcoGpuAbi.BufferHandle.Null))!;
            Assert.That(nullError.Message, Does.Contain("handle"));
            AlcoGpuAbi.ErrorInfo error = default;
            AlcoGpuNative.GetLastError(ref error);
            Assert.That(error.Status, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));
            string? nullMessage = AlcoGpuMarshal.BorrowedString(error.Message);
            Assert.That(nullMessage, Is.Not.Null.And.Not.Empty);
            byte* nullMessagePointer = error.Message;

            AlcoGpuAbi.BufferDesc descriptor = new() { Size = 64, Usage = (uint)BufferUsage.CopyDst };
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
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.False, 0, &queueEmpty);
            AlcoGpuAbi.DeviceMessage message = default;
            Assert.That(AlcoGpuNative.DevicePopMessage(device, ref message), Is.EqualTo(AlcoGpuAbi.Status.NotReady));
            AlcoGpuAbi.BuildInfo buildInfo = default;
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
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        var sources = new AlcoGpuAbi.BufferHandle[count];
        var destinations = new AlcoGpuAbi.BufferHandle[count];
        AlcoGpuAbi.EncoderHandle encoder = AlcoGpuAbi.EncoderHandle.Null;
        AlcoGpuAbi.CommandBufferHandle commands = AlcoGpuAbi.CommandBufferHandle.Null;
        AlcoGpuAbi.BufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.CopySrc | BufferUsage.CopyDst) };
        AlcoGpuAbi.BufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
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
                AlcoGpuAbi.EncoderHandle finishedEncoder = encoder;
                encoder = AlcoGpuAbi.EncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(finishedEncoder, out commands);
                AlcoGpuAbi.CommandBufferHandle submitted = commands;
                commands = AlcoGpuAbi.CommandBufferHandle.Null;
                ulong submissionIndex = 0;
                AlcoGpuNative.QueueSubmit(device, submitted, &submissionIndex);
                for (int i = 0; i < count; i++)
                {
                    AlcoGpuNative.BufferMapRead(destinations[i], 0, size);
                }
                uint queueEmpty = 0;
                AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.True, submissionIndex, &queueEmpty);
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
                    AlcoGpuAbi.BufferHandle destination = destinations[i];
                    destinations[i] = AlcoGpuAbi.BufferHandle.Null;
                    AlcoGpuNative.BufferDestroy(destination);
                    AlcoGpuAbi.BufferHandle source = sources[i];
                    sources[i] = AlcoGpuAbi.BufferHandle.Null;
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
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        var sources = new AlcoGpuAbi.BufferHandle[count];
        var destinations = new AlcoGpuAbi.BufferHandle[count];
        var encoders = new AlcoGpuAbi.EncoderHandle[count];
        var commands = new AlcoGpuAbi.CommandBufferHandle[count];
        byte* written = stackalloc byte[size];
        try
        {
            for (int i = 0; i < count; i++)
            {
                AlcoGpuAbi.BufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.CopySrc | BufferUsage.CopyDst) };
                AlcoGpuAbi.BufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
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
                AlcoGpuAbi.EncoderHandle encoder = encoders[i];
                encoders[i] = AlcoGpuAbi.EncoderHandle.Null;
                AlcoGpuNative.EncoderFinish(encoder, out commands[i]);
            }
            ulong submissionIndex = 0;
            fixed (AlcoGpuAbi.CommandBufferHandle* submitted = commands)
            {
                AlcoGpuNative.QueueSubmitBatch(device, submitted, count, &submissionIndex);
            }
            for (int i = 0; i < count; i++)
            {
                // Submit consumed every wrapper; only the buffers remain to clean up.
                commands[i] = AlcoGpuAbi.CommandBufferHandle.Null;
                AlcoGpuNative.BufferMapRead(destinations[i], 0, size);
            }
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.True, submissionIndex, &queueEmpty);
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
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        AlcoGpuAbi.BufferHandle source = AlcoGpuAbi.BufferHandle.Null;
        AlcoGpuAbi.BufferHandle destination = AlcoGpuAbi.BufferHandle.Null;
        AlcoGpuAbi.EncoderHandle encoder = AlcoGpuAbi.EncoderHandle.Null;
        AlcoGpuAbi.CommandBufferHandle command = AlcoGpuAbi.CommandBufferHandle.Null;
        AlcoGpuAbi.BufferDesc sourceDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapWrite | BufferUsage.CopySrc) };
        AlcoGpuAbi.BufferDesc destinationDescriptor = new() { Size = size, Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst) };
        try
        {
            AlcoGpuNative.BufferCreate(device, in sourceDescriptor, out source);
            AlcoGpuNative.BufferCreate(device, in destinationDescriptor, out destination);
            AlcoGpuNative.BufferMapWrite(source, 0, size);
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.True, ulong.MaxValue, &queueEmpty);
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
            AlcoGpuAbi.EncoderHandle finishedEncoder = encoder;
            encoder = AlcoGpuAbi.EncoderHandle.Null;
            AlcoGpuNative.EncoderFinish(finishedEncoder, out command);
            ulong submissionIndex = 0;
            AlcoGpuNative.QueueSubmit(device, command, &submissionIndex);
            command = AlcoGpuAbi.CommandBufferHandle.Null;
            uint submitQueueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.True, submissionIndex, &submitQueueEmpty);

            AlcoGpuNative.BufferMapRead(destination, 0, size);
            uint readQueueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.True, ulong.MaxValue, &readQueueEmpty);
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
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        AlcoGpuAbi.BindGroupLayoutHandle layout = AlcoGpuAbi.BindGroupLayoutHandle.Null;
        AlcoGpuAbi.BindGroupHandle group = AlcoGpuAbi.BindGroupHandle.Null;
        AlcoGpuAbi.BindGroupLayoutEntry layoutEntry = default;
        AlcoGpuAbi.BindGroupEntry groupEntry = default;
        try
        {
            AlcoGpuAbi.BindGroupLayoutDesc layoutDescriptor = new() { Entries = useNullEntries ? null : &layoutEntry };
            AlcoGpuNative.BindGroupLayoutCreate(device, in layoutDescriptor, out layout);
            AlcoGpuAbi.BindGroupDesc groupDescriptor = new() { Layout = layout, Entries = useNullEntries ? null : &groupEntry };
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
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        AlcoGpuAbi.TextureHandle texture = AlcoGpuAbi.TextureHandle.Null;
        AlcoGpuAbi.TextureViewHandle view = AlcoGpuAbi.TextureViewHandle.Null;
        AlcoGpuAbi.EncoderHandle encoder = AlcoGpuAbi.EncoderHandle.Null;
        AlcoGpuAbi.RenderPassHandle pass = AlcoGpuAbi.RenderPassHandle.Null;
        try
        {
            AlcoGpuAbi.TextureDesc textureDescriptor = new()
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
            AlcoGpuAbi.DepthStencilAttachment depth = new()
            {
                View = view,
                DepthLoadOp = 1,
                DepthStoreOp = 0,
                DepthClear = invalidStencil ? 1f : 2f,
                StencilLoadOp = invalidStencil ? 0 : AlcoGpuAbi.None,
                StencilStoreOp = invalidStencil ? 0 : AlcoGpuAbi.None,
            };
            AlcoGpuAbi.RenderPassDesc descriptor = new() { DepthStencil = &depth };
            AlcoGpuNative.RenderPassBegin(encoder, in descriptor, out pass);
            AlcoGpuAbi.RenderPassHandle endedPass = pass;
            pass = AlcoGpuAbi.RenderPassHandle.Null;
            AlcoGpuNative.RenderPassEnd(endedPass);
            AlcoGpuAbi.EncoderHandle finishedEncoder = encoder;
            encoder = AlcoGpuAbi.EncoderHandle.Null;
            AlcoGpuAbi.CommandBufferHandle buffer = AlcoGpuAbi.CommandBufferHandle.Null;
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

    private static AlcoGpuAbi.DeviceHandle CreateNativeDevice()
    {
        AlcoGpuAbi.DeviceDesc descriptor = new()
        {
            Backend = AlcoGpuAbi.BackendRequest.Auto,
            Debug = AlcoGpuAbi.True,
            PushConstantsSize = 128,
        };
        AlcoGpuNative.DeviceCreate(in descriptor, out AlcoGpuAbi.DeviceHandle device);
        return device;
    }

    /// <summary>Verifies nonblocking polls and repeated empty-message polls remain exception-free.</summary>
    [Test]
    public void PollAndEmptyMessageQueueAreSafe()
    {
        AlcoGpuAbi.DeviceHandle device = CreateNativeDevice();
        try
        {
            uint queueEmpty = 0;
            AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.False, 0, &queueEmpty);
            AlcoGpuAbi.DeviceMessage message = default;
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
