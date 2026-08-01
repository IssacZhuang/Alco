using Alco.Graphics.WebGPU;
using NUnit.Framework;
using WebGPU;

namespace Alco.Graphics.Test;

[TestFixture]
public sealed class WebGPUBufferUsageTests
{
    [Test]
    public void QueryResolveFlagIsPreserved()
    {
        WGPUBufferUsage converted = WebGPUUtility.ConvertBufferUsage(
            BufferUsage.QueryResolve | BufferUsage.CopySrc);

        Assert.That((converted & WGPUBufferUsage.QueryResolve) != 0, Is.True);
        Assert.That((converted & WGPUBufferUsage.CopySrc) != 0, Is.True);
    }
}
