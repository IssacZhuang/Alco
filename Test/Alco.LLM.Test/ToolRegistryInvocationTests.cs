using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NUnit.Framework;

namespace Alco.LLM.Test;

[TestFixture]
public class ToolRegistryInvocationTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [SetUp]
    public void SetUp()
    {
        FakeAdvancedToolFunctions.Reset();
    }

    [Test]
    public async Task InvokeToolAsync_ReturnsValueDirectly()
    {
        var registry = CreateRegistry();
        var result = await registry.InvokeToolAsync(
            "Add",
            JsonSerializer.SerializeToElement(new { a = 2, b = 4 }));

        Assert.That(result, Is.EqualTo(6));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_VoidResult_ReturnsValue()
    {
        var registry = CreateRegistry();
        var result = await registry.InvokeToolAsync(
            "Complete",
            JsonSerializer.SerializeToElement(new { }));

        Assert.That(result, Is.EqualTo("done"));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_TaskResult_AwaitsAndReturnsValue()
    {
        var registry = CreateRegistry();
        var result = await registry.InvokeToolAsync(
            "AddAsync",
            JsonSerializer.SerializeToElement(new { a = 5, b = 8 }));

        Assert.That(result, Is.EqualTo(13));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_TaskWithoutResult_AwaitsAndReturnsNull()
    {
        var registry = CreateRegistry();
        var result = await registry.InvokeToolAsync(
            "CompleteTaskAsync",
            JsonSerializer.SerializeToElement(new { }));

        Assert.That(result, Is.Null);
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_ValueTaskResult_AwaitsAndReturnsValue()
    {
        var registry = CreateRegistry();
        var result = await registry.InvokeToolAsync(
            "AddValueTaskAsync",
            JsonSerializer.SerializeToElement(new { a = 6, b = 9 }));

        Assert.That(result, Is.EqualTo(15));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_ValueTaskWithoutResult_AwaitsAndReturnsNull()
    {
        var registry = CreateRegistry();
        var result = await registry.InvokeToolAsync(
            "CompleteValueTaskAsync",
            JsonSerializer.SerializeToElement(new { }));

        Assert.That(result, Is.Null);
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_MainThreadTool_WaitsUntilQueueIsDrained()
    {
        var registry = CreateRegistry();
        var task = registry.InvokeToolAsync(
            "MainThreadAdd",
            JsonSerializer.SerializeToElement(new { a = 3, b = 7 }));

        Assert.That(task.IsCompleted, Is.False);

        registry.DrainMainThreadQueue();
        var result = await task;

        Assert.That(result, Is.EqualTo(10));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvokeToolAsync_MainThreadTaskResult_AwaitsAfterQueueIsDrained()
    {
        var registry = CreateRegistry();
        var task = registry.InvokeToolAsync(
            "MainThreadAddAsync",
            JsonSerializer.SerializeToElement(new { a = 4, b = 11 }));

        Assert.That(task.IsCompleted, Is.False);

        registry.DrainMainThreadQueue();
        var result = await task;

        Assert.That(result, Is.EqualTo(15));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(1));
    }

    private static ToolRegistry CreateRegistry()
    {
        return new ToolRegistry([typeof(FakeAdvancedToolFunctions)], null, JsonOptions);
    }
}
