using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Alco.Engine;
using Alco.IO;

namespace Alco.Engine.Test;

[TestFixture]
public class TestJsonPreprocessor
{
    private class TestFileSource : IFileSource
    {
        public string Name => "Test File Source";

        private readonly Dictionary<string, byte[]> _files = new();

        public int Priority => 0;

        public IEnumerable<string> AllFileNames => _files.Keys;

        public void AddFile(string filename, string content)
        {
            _files[filename] = Encoding.UTF8.GetBytes(content);
        }

        public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string failureReason)
        {
            if (_files.TryGetValue(path, out var bytes))
            {
                data = new SafeMemoryHandle(bytes);
                failureReason = null;
                return true;
            }

            data = SafeMemoryHandle.Empty;
            failureReason = $"File not found: {path}";
            return false;
        }

        public void Dispose()
        {
        }
    }

    private JsonPreprocessor CreatePreprocessor(out List<string> infos, out List<string> warnings, out List<string> errors)
    {
        infos = new List<string>();
        warnings = new List<string>();
        errors = new List<string>();

        var infosList = infos;
        var warningsList = warnings;
        var errorsList = errors;

        return new JsonPreprocessor(
            info => infosList.Add(info),
            warning => warningsList.Add(warning),
            error => errorsList.Add(error)
        );
    }

    [Test]
    public void TestBasicJsonLoading()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("test1.json", @"{""Id"": ""test1"", ""name"": ""Test Object 1""}");
        fileSource.AddFile("test2.json", @"{""Id"": ""test2"", ""name"": ""Test Object 2""}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("test1", out var doc1), Is.True);
        Assert.That(preprocessor.TryGetJsonDocument("test2", out var doc2), Is.True);

        Assert.That(doc1.RootElement.GetProperty("name").GetString(), Is.EqualTo("Test Object 1"));
        Assert.That(doc2.RootElement.GetProperty("name").GetString(), Is.EqualTo("Test Object 2"));
    }

    [Test]
    public void TestAbstractJsonLoading()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("abstract1.json", @"{""$abstract"": true, ""Id"": ""abstract1"", ""baseValue"": 100}");
        fileSource.AddFile("concrete1.json", @"{""Id"": ""concrete1"", ""$parent"": ""abstract1"", ""name"": ""Concrete Object""}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("concrete1", out var doc), Is.True);

        var rootElement = doc.RootElement;
        Assert.That(rootElement.GetProperty("name").GetString(), Is.EqualTo("Concrete Object"));
        Assert.That(rootElement.GetProperty("baseValue").GetInt32(), Is.EqualTo(100));

        // Abstract documents should not be accessible directly through GetJsonDocument
        Assert.That(preprocessor.TryGetJsonDocument("abstract1", out _), Is.False);
    }

    [Test]
    public void TestInheritanceChain()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("base.json", @"{""$abstract"": true, ""Id"": ""base"", ""value1"": 1, ""value2"": 2}");
        fileSource.AddFile("middle.json", @"{""$abstract"": true, ""Id"": ""middle"", ""$parent"": ""base"", ""value2"": 20, ""value3"": 3}");
        fileSource.AddFile("final.json", @"{""Id"": ""final"", ""$parent"": ""middle"", ""value3"": 30, ""value4"": 4}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("final", out var doc), Is.True);

        var rootElement = doc.RootElement;
        Assert.That(rootElement.GetProperty("value1").GetInt32(), Is.EqualTo(1), "Should inherit from base");
        Assert.That(rootElement.GetProperty("value2").GetInt32(), Is.EqualTo(20), "Should inherit from middle");
        Assert.That(rootElement.GetProperty("value3").GetInt32(), Is.EqualTo(30), "Should override from final");
        Assert.That(rootElement.GetProperty("value4").GetInt32(), Is.EqualTo(4), "Should have final's own property");
    }

    [Test]
    public void TestArrayInheritance()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("parent.json", @"{""$abstract"": true, ""Id"": ""parent"", ""items"": [""a"", ""b""]}");
        fileSource.AddFile("child.json", @"{""Id"": ""child"", ""$parent"": ""parent"", ""items"": [{""$inherit"": ""append""}, ""c"", ""d""]}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("child", out var doc), Is.True);

        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.That(items.Length, Is.EqualTo(4));
        Assert.That(items[0].GetString(), Is.EqualTo("a"));
        Assert.That(items[1].GetString(), Is.EqualTo("b"));
        Assert.That(items[2].GetString(), Is.EqualTo("c"));
        Assert.That(items[3].GetString(), Is.EqualTo("d"));
    }

    [Test]
    public void TestObjectInheritanceControl()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("parent.json", @"{""$abstract"": true, ""Id"": ""parent"", ""data"": {""a"": 1, ""b"": 2}}");
        fileSource.AddFile("child.json", @"{""Id"": ""child"", ""$parent"": ""parent"", ""data"": {""$inherit"": false, ""c"": 3}}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("child", out var doc), Is.True);

        var data = doc.RootElement.GetProperty("data");
        Assert.That(data.TryGetProperty("a", out _), Is.False, "Should not inherit 'a'");
        Assert.That(data.TryGetProperty("b", out _), Is.False, "Should not inherit 'b'");
        Assert.That(data.GetProperty("c").GetInt32(), Is.EqualTo(3), "Should have 'c'");
    }

    [Test]
    public void TestCircularDependencyDetection()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("a.json", @"{""Id"": ""a"", ""$parent"": ""b"", ""value"": 1}");
        fileSource.AddFile("b.json", @"{""Id"": ""b"", ""$parent"": ""c"", ""value"": 2}");
        fileSource.AddFile("c.json", @"{""Id"": ""c"", ""$parent"": ""a"", ""value"": 3}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors.Count, Is.GreaterThan(0), "Should detect circular dependency");
        Assert.That(errors.Any(e => e.Contains("Circular dependency")), Is.True);
    }

    [Test]
    public void TestMissingParent()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("child.json", @"{""Id"": ""child"", ""$parent"": ""nonexistent"", ""value"": 1}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors.Count, Is.GreaterThan(0), "Should report missing parent");
        Assert.That(errors.Any(e => e.Contains("Parent with ID 'nonexistent' not found")), Is.True);
    }

    [Test]
    public void TestDuplicateIds()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("duplicate1.json", @"{""Id"": ""same"", ""value"": 1}");
        fileSource.AddFile("duplicate2.json", @"{""Id"": ""same"", ""value"": 2}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors.Count, Is.GreaterThan(0), "Should report duplicate IDs");
        Assert.That(errors.Any(e => e.Contains("duplicate id same")), Is.True);
    }

    [Test]
    public void TestMissingId()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("no-id.json", @"{""value"": 1}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors.Count, Is.GreaterThan(0), "Should report missing ID");
        Assert.That(errors.Any(e => e.Contains("has no id")), Is.True);
    }

    [Test]
    public void TestEmptyParentId()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("empty-parent.json", @"{""Id"": ""test"", ""$parent"": """", ""value"": 1}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(warnings.Count, Is.GreaterThan(0), "Should report empty parent ID as warning");
        Assert.That(warnings.Any(w => w.Contains("empty parent ID")), Is.True);
    }

    [Test]
    public void TestGetJsonDocumentException()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);

        // Act & Assert
        Assert.Throws<Exception>(() => preprocessor.GetJsonDocument("nonexistent"),
            "Should throw exception for non-existent document");
    }

    [Test]
    public void TestNonAbstractParentInheritance()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        fileSource.AddFile("parent.json", @"{""Id"": ""parent"", ""baseValue"": 100}");
        fileSource.AddFile("child.json", @"{""Id"": ""child"", ""$parent"": ""parent"", ""childValue"": 200}");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("child", out var doc), Is.True);

        var rootElement = doc.RootElement;
        Assert.That(rootElement.GetProperty("baseValue").GetInt32(), Is.EqualTo(100));
        Assert.That(rootElement.GetProperty("childValue").GetInt32(), Is.EqualTo(200));

        // Parent should still be accessible since it's not abstract
        Assert.That(preprocessor.TryGetJsonDocument("parent", out var parentDoc), Is.True);
        Assert.That(parentDoc.RootElement.GetProperty("baseValue").GetInt32(), Is.EqualTo(100));
    }

    [Test]
    public void TestFileSourceManagement()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource1 = new TestFileSource();
        var fileSource2 = new TestFileSource();

        fileSource1.AddFile("test1.json", @"{""Id"": ""test1"", ""source"": 1}");
        fileSource2.AddFile("test2.json", @"{""Id"": ""test2"", ""source"": 2}");

        // Act & Assert - Add sources
        preprocessor.AddFileSource(fileSource1);
        preprocessor.AddFileSource(fileSource2);
        preprocessor.Preprocess();

        Assert.That(preprocessor.TryGetJsonDocument("test1", out _), Is.True);
        Assert.That(preprocessor.TryGetJsonDocument("test2", out _), Is.True);

        // Clear and verify
        preprocessor.RemoveFileSource(fileSource1);
        // Need to recreate preprocessor to test removal (current implementation doesn't support dynamic removal)
    }

    [Test]
    public void TestComplexInheritanceScenario()
    {
        // Arrange
        var preprocessor = CreatePreprocessor(out var infos, out var warnings, out var errors);
        var fileSource = new TestFileSource();

        // Create a complex inheritance scenario with arrays, objects, and multiple levels
        fileSource.AddFile("base.json", @"{
            ""$abstract"": true,
            ""Id"": ""base"",
            ""properties"": {
                ""health"": 100,
                ""damage"": 10
            },
            ""abilities"": [""move"", ""attack""]
        }");

        fileSource.AddFile("warrior.json", @"{
            ""$abstract"": true,
            ""Id"": ""warrior"",
            ""$parent"": ""base"",
            ""properties"": {
                ""damage"": 20,
                ""armor"": 5
            },
            ""abilities"": [{""$inherit"": ""append""}, ""block"", ""charge""]
        }");

        fileSource.AddFile("paladin.json", @"{
            ""Id"": ""paladin"",
            ""$parent"": ""warrior"",
            ""properties"": {
                ""mana"": 50,
                ""healing"": 15
            },
            ""abilities"": [{""$inherit"": ""append""}, ""heal"", ""bless""]
        }");

        preprocessor.AddFileSource(fileSource);

        // Act
        preprocessor.Preprocess();

        // Assert
        Assert.That(errors, Is.Empty, "Should have no errors");
        Assert.That(preprocessor.TryGetJsonDocument("paladin", out var doc), Is.True);

        var rootElement = doc.RootElement;

        // Check properties inheritance and overriding
        var properties = rootElement.GetProperty("properties");
        Assert.That(properties.GetProperty("health").GetInt32(), Is.EqualTo(100), "Should inherit from base");
        Assert.That(properties.GetProperty("damage").GetInt32(), Is.EqualTo(20), "Should inherit from warrior");
        Assert.That(properties.GetProperty("armor").GetInt32(), Is.EqualTo(5), "Should inherit from warrior");
        Assert.That(properties.GetProperty("mana").GetInt32(), Is.EqualTo(50), "Should have paladin's own property");
        Assert.That(properties.GetProperty("healing").GetInt32(), Is.EqualTo(15), "Should have paladin's own property");

        // Check array inheritance
        var abilities = rootElement.GetProperty("abilities").EnumerateArray().Select(e => e.GetString()).ToArray();
        var expectedAbilities = new[] { "move", "attack", "block", "charge", "heal", "bless" };
        Assert.That(abilities, Is.EqualTo(expectedAbilities), "Should have all abilities in correct order");
    }
}