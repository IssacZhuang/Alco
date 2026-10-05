using Alco.Graphics.AlcoGpu.Interop;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Round-trip tests of the alco-gpu C ABI: version handshake, real device
/// creation on the local adapter, info queries and the double-destroy error
/// contract (which must surface as a catchable status, not a process abort).
/// </summary>
[TestFixture]
[Category("AlcoGpu")]
public unsafe class AlcoGpuAbiTests
{
    [Test]
    public void AbiVersionMatches()
    {
        uint version = AlcoGpuNative.AbiVersion();
        Assert.That(version >> 16, Is.EqualTo(AlcoGpuAbi.AbiMajor));
        Assert.That(version & 0xFFFF, Is.GreaterThanOrEqualTo(AlcoGpuAbi.AbiMinor));
    }

    [Test]
    public void BuildInfoCarriesWgpuVersion()
    {
        AlcoBuildInfo info = default;
        AlcoGpuNative.BuildInfo(ref info);
        Assert.That(info.WgpuVersion >> 16, Is.EqualTo(30));
        string? build = AlcoGpuMarshal.BorrowedString(info.AlcoBuild);
        Assert.That(build, Is.Not.Null.And.Contains("alco-gpu"));
    }

    [Test]
    public void DeviceCreateGetInfoDestroyRoundTrip()
    {
        AlcoDeviceDesc desc = default;
        desc.Backend = AlcoGpuAbi.BackendRequest.Auto;
        desc.Debug = AlcoGpuAbi.AlcoTrue;
        desc.RequiredFeatures = 0;
        desc.PushConstantsSize = 128;
        desc.Name = null;

        uint status = AlcoGpuNative.DeviceCreate(in desc, out AlcoHandle device);
        AlcoGpuMarshal.ThrowIfFailed(status);
        Assert.That(device.IsNull, Is.False);

        try
        {
            AlcoDeviceInfo info = default;
            status = AlcoGpuNative.DeviceGetInfo(device, ref info);
            AlcoGpuMarshal.ThrowIfFailed(status);
            Assert.That(info.Backend, Is.EqualTo(AlcoGpuAbi.BackendResolved.Vulkan).Or.EqualTo(AlcoGpuAbi.BackendResolved.Dx12));
            Assert.That(info.Caps & AlcoGpuAbi.Caps.PassthroughShaders, Is.Not.Zero, "passthrough shaders expected on desktop adapters");
            Assert.That(info.MaxBindGroups, Is.GreaterThan(0));
            Assert.That(info.MaxImmediateSize, Is.EqualTo(128));
            Assert.That(AlcoGpuMarshal.BorrowedString(info.AdapterName), Is.Not.Null.And.Not.Empty);
        }
        finally
        {
            status = AlcoGpuNative.DeviceDestroy(device);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    [Test]
    public void DoubleDestroyReturnsInvalidHandleInsteadOfCrashing()
    {
        AlcoDeviceDesc desc = default;
        desc.Backend = AlcoGpuAbi.BackendRequest.Auto;
        desc.Debug = AlcoGpuAbi.AlcoFalse;
        desc.PushConstantsSize = 128;

        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.DeviceCreate(in desc, out AlcoHandle device));
        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.DeviceDestroy(device));

        // The second destroy must be a clean, catchable error — this exact
        // scenario aborted the process under wgpu-native.
        uint status = AlcoGpuNative.DeviceDestroy(device);
        Assert.That(status, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));

        AlcoErrorInfo error = default;
        AlcoGpuNative.GetLastError(ref error);
        Assert.That(error.Status, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));
        Assert.That(AlcoGpuMarshal.BorrowedString(error.Message), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void PollAndEmptyMessageQueueAreSafe()
    {
        AlcoDeviceDesc desc = default;
        desc.Backend = AlcoGpuAbi.BackendRequest.Auto;
        desc.PushConstantsSize = 128;

        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.DeviceCreate(in desc, out AlcoHandle device));
        try
        {
            uint queueEmpty = 0;
            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.DevicePoll(device, AlcoGpuAbi.AlcoFalse, 0, &queueEmpty));

            AlcoDeviceMessage message = default;
            uint status = AlcoGpuNative.DevicePopMessage(device, ref message);
            Assert.That(status, Is.EqualTo(AlcoGpuAbi.Status.NotReady));
        }
        finally
        {
            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.DeviceDestroy(device));
        }
    }
}
