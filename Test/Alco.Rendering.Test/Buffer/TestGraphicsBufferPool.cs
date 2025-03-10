using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

public class TestGraphicsBufferPool
{
    [Test(Description = "Test GraphicsBufferPool initialization")]
    public void TestGraphicsBufferPoolInitialization()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;

        // Act
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Assert
        Assert.That(bufferPool.BufferSizes.Length, Is.EqualTo(4));
        Assert.That(bufferPool.BufferSizes[0], Is.EqualTo(128));
        Assert.That(bufferPool.BufferSizes[1], Is.EqualTo(256));
        Assert.That(bufferPool.BufferSizes[2], Is.EqualTo(512));
        Assert.That(bufferPool.BufferSizes[3], Is.EqualTo(1024));
    }

    [Test(Description = "Test TryGetBuffer with exact size match")]
    public void TestTryGetBufferExactMatch()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Act
        bool result = bufferPool.TryGetBuffer(256, out var buffer);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer.Size, Is.EqualTo(256));
    }

    [Test(Description = "Test TryGetBuffer with size that needs rounding up")]
    public void TestTryGetBufferRoundUp()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Act
        bool result = bufferPool.TryGetBuffer(200, out var buffer);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer.Size, Is.EqualTo(256));
    }

    [Test(Description = "Test TryGetBuffer with size larger than any pool")]
    public void TestTryGetBufferTooLarge()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Act
        bool result = bufferPool.TryGetBuffer(2048, out var buffer);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(buffer, Is.Null);
    }

    [Test(Description = "Test TryReturnBuffer with matching size")]
    public void TestTryReturnBufferMatchingSize()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Get a buffer from the pool
        bufferPool.TryGetBuffer(256, out var buffer);

        // Act
        bool result = bufferPool.TryReturnBuffer(buffer);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test(Description = "Test TryReturnBuffer with non-matching size")]
    public void TestTryReturnBufferNonMatchingSize()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Create a buffer with a size not in the pool
        var buffer = renderingSystem.CreateGraphicsBuffer(300);

        // Act
        bool result = bufferPool.TryReturnBuffer(buffer);

        // Assert
        Assert.That(result, Is.False);

        // Clean up
        buffer.Dispose();
    }

    [Test(Description = "Test TryGetEntry with exact size match")]
    public void TestTryGetEntryExactMatch()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Act
        bool result = bufferPool.TryGetEntry(256, out var entry);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(entry.BufferSize, Is.EqualTo(256));
    }

    [Test(Description = "Test TryGetEntry with size that needs rounding up")]
    public void TestTryGetEntryRoundUp()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Act
        bool result = bufferPool.TryGetEntry(200, out var entry);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(entry.BufferSize, Is.EqualTo(256));
    }

    [Test(Description = "Test buffer reuse from pool")]
    public void TestBufferReuse()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        // Get a buffer and return it
        bufferPool.TryGetBuffer(256, out var buffer1);
        bufferPool.TryReturnBuffer(buffer1);

        // Act - Get another buffer of the same size
        bufferPool.TryGetBuffer(256, out var buffer2);

        // Assert - Should be the same buffer instance
        Assert.That(buffer2, Is.SameAs(buffer1));
    }

    [Test(Description = "Test Entry.Get and Entry.TryReturn")]
    public void TestEntryGetAndReturn()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        bufferPool.TryGetEntry(256, out var entry);

        // Act
        var buffer = entry.Get();
        bool returnResult = entry.TryReturn(buffer);

        // Assert
        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer.Size, Is.EqualTo(256));
        Assert.That(returnResult, Is.True);
    }

    [Test(Description = "Test Entry.TryReturn with wrong size buffer")]
    public void TestEntryTryReturnWrongSize()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        using var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256, 512, 1024 });

        bufferPool.TryGetEntry(256, out var entry);
        var buffer = renderingSystem.CreateGraphicsBuffer(512);

        // Act
        bool returnResult = entry.TryReturn(buffer);

        // Assert
        Assert.That(returnResult, Is.False);

        // Clean up
        buffer.Dispose();
    }

    [Test(Description = "Test pool disposal")]
    public void TestPoolDisposal()
    {
        // Arrange
        using var host = Utility.CreateRenderingSystem();
        var renderingSystem = host.RenderingSystem;
        var bufferPool = new GraphicsBufferPool(renderingSystem, new uint[] { 128, 256 });

        // Get some buffers
        bufferPool.TryGetBuffer(128, out var buffer1);
        bufferPool.TryGetBuffer(256, out var buffer2);

        // Return one buffer
        bufferPool.TryReturnBuffer(buffer1);

        // Act - Dispose the pool
        bufferPool.Dispose();

        // Assert - No way to directly test disposal, but we can verify the pool is disposed
        // by checking that it doesn't throw when disposed
        Assert.DoesNotThrow(() => bufferPool.Dispose());
    }
}