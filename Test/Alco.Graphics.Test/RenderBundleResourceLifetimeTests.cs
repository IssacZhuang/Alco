using System.Runtime.CompilerServices;
using Alco.Graphics.NoGPU;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Verifies the managed resource ownership required by reusable render bundles.
/// </summary>
[TestFixture]
public sealed class RenderBundleResourceLifetimeTests
{
    [Test(Description = "A recorded render bundle retains its resource groups until it is replaced")]
    public void RecordedResourceGroupRemainsAliveUntilBundleIsReRecorded()
    {
        NoDevice device = NoDevice.noDevice;
        GPURenderBundle bundle = device.CreateRenderBundle(new RenderBundleDescriptor("resource_lifetime_test"));
        GPUAttachmentLayout attachmentLayout = device.CreateAttachmentLayout(
            new AttachmentLayoutDescriptor(Array.Empty<ColorAttachment>(), null, "resource_lifetime_test"));

        WeakReference resourceGroup = RecordResourceGroup(device, bundle, attachmentLayout);
        CollectGarbage();

        Assert.That(resourceGroup.IsAlive, Is.True, "The active bundle must retain its recorded resource group");

        bundle.Begin(attachmentLayout);
        bundle.End();
        CollectGarbage();

        Assert.That(resourceGroup.IsAlive, Is.False, "Replacing the bundle must release resources no longer recorded");

        GC.KeepAlive(bundle);
        GC.KeepAlive(attachmentLayout);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RecordResourceGroup(
        NoDevice device,
        GPURenderBundle bundle,
        GPUAttachmentLayout attachmentLayout)
    {
        GPUResourceGroup resourceGroup = device.CreateResourceGroup(
            new ResourceGroupDescriptor(
                device.BindGroupUniformBuffer,
                Array.Empty<ResourceBindingEntry>(),
                "resource_lifetime_test"));
        var weakReference = new WeakReference(resourceGroup);

        bundle.Begin(attachmentLayout);
        bundle.SetGraphicsResources(0, resourceGroup);
        bundle.End();

        return weakReference;
    }

    private static void CollectGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
