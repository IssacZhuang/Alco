using System;
using System.Text.Json;
using NUnit.Framework;

namespace Alco.Engine.Test;

[TestFixture]
public class TestUtilsJson
{
    [Test]
    public void Merge_SimpleObjects_ShouldMergeCorrectly()
    {
        // Arrange
        var parentJson = """{"name": "parent", "age": 30}""";
        var targetJson = """{"age": 25, "city": "New York"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("parent"));
        Assert.That(resultDoc.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(25)); // target overrides
        Assert.That(resultDoc.RootElement.GetProperty("city").GetString(), Is.EqualTo("New York"));
    }

    [Test]
    public void Merge_NestedObjects_ShouldMergeRecursively()
    {
        // Arrange
        var parentJson = """{"user": {"name": "John", "age": 30}, "active": true}""";
        var targetJson = """{"user": {"age": 25, "email": "john@example.com"}, "role": "admin"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var userElement = resultDoc.RootElement.GetProperty("user");
        Assert.That(userElement.GetProperty("name").GetString(), Is.EqualTo("John"));
        Assert.That(userElement.GetProperty("age").GetInt32(), Is.EqualTo(25)); // target overrides
        Assert.That(userElement.GetProperty("email").GetString(), Is.EqualTo("john@example.com"));
        Assert.That(resultDoc.RootElement.GetProperty("active").GetBoolean(), Is.True);
        Assert.That(resultDoc.RootElement.GetProperty("role").GetString(), Is.EqualTo("admin"));
    }

    [Test]
    public void Merge_Arrays_ShouldConcatenate()
    {
        // Arrange
        var parentJson = """[1, 2, 3]""";
        var targetJson = """[4, 5, 6]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(6));
        Assert.That(array[0].GetInt32(), Is.EqualTo(1));
        Assert.That(array[1].GetInt32(), Is.EqualTo(2));
        Assert.That(array[2].GetInt32(), Is.EqualTo(3));
        Assert.That(array[3].GetInt32(), Is.EqualTo(4));
        Assert.That(array[4].GetInt32(), Is.EqualTo(5));
        Assert.That(array[5].GetInt32(), Is.EqualTo(6));
    }

    [Test]
    public void Merge_ObjectsWithArrays_ShouldMergeArraysCorrectly()
    {
        // Arrange
        var parentJson = """{"tags": ["tag1", "tag2"], "name": "parent"}""";
        var targetJson = """{"tags": ["tag3", "tag4"], "description": "target"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var tagsArray = resultDoc.RootElement.GetProperty("tags").EnumerateArray().ToArray();
        Assert.That(tagsArray.Length, Is.EqualTo(4));
        Assert.That(tagsArray[0].GetString(), Is.EqualTo("tag1"));
        Assert.That(tagsArray[1].GetString(), Is.EqualTo("tag2"));
        Assert.That(tagsArray[2].GetString(), Is.EqualTo("tag3"));
        Assert.That(tagsArray[3].GetString(), Is.EqualTo("tag4"));
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("parent"));
        Assert.That(resultDoc.RootElement.GetProperty("description").GetString(), Is.EqualTo("target"));
    }

    [Test]
    public void Merge_TypeMismatch_ShouldOverrideWithTarget()
    {
        // Arrange
        var parentJson = """{"value": {"nested": "object"}}""";
        var targetJson = """{"value": "string"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("value").GetString(), Is.EqualTo("string"));
    }

    [Test]
    public void Merge_NullValueInTarget_ShouldKeepParentValue()
    {
        // Arrange
        var parentJson = """{"name": "John", "age": 30}""";
        var targetJson = """{"name": null, "city": "New York"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("John"));
        Assert.That(resultDoc.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(30));
        Assert.That(resultDoc.RootElement.GetProperty("city").GetString(), Is.EqualTo("New York"));
    }

    [Test]
    public void Merge_EmptyObjects_ShouldReturnEmptyObject()
    {
        // Arrange
        var parentJson = """{}""";
        var targetJson = """{}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(resultDoc.RootElement.EnumerateObject().Count(), Is.EqualTo(0));
    }

    [Test]
    public void Merge_EmptyArrays_ShouldReturnEmptyArray()
    {
        // Arrange
        var parentJson = """[]""";
        var targetJson = """[]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(resultDoc.RootElement.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public void Merge_ComplexNestedStructure_ShouldMergeCorrectly()
    {
        // Arrange
        var parentJson = """
        {
            "config": {
                "database": {
                    "host": "localhost",
                    "port": 5432
                },
                "features": ["auth", "logging"]
            },
            "version": "1.0"
        }
        """;

        var targetJson = """
        {
            "config": {
                "database": {
                    "port": 3306,
                    "name": "mydb"
                },
                "features": ["cache"],
                "timeout": 30
            },
            "author": "test"
        }
        """;

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var configElement = resultDoc.RootElement.GetProperty("config");
        var databaseElement = configElement.GetProperty("database");

        Assert.That(databaseElement.GetProperty("host").GetString(), Is.EqualTo("localhost"));
        Assert.That(databaseElement.GetProperty("port").GetInt32(), Is.EqualTo(3306)); // target overrides
        Assert.That(databaseElement.GetProperty("name").GetString(), Is.EqualTo("mydb"));

        var featuresArray = configElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.That(featuresArray.Length, Is.EqualTo(3));
        Assert.That(featuresArray[0].GetString(), Is.EqualTo("auth"));
        Assert.That(featuresArray[1].GetString(), Is.EqualTo("logging"));
        Assert.That(featuresArray[2].GetString(), Is.EqualTo("cache"));

        Assert.That(configElement.GetProperty("timeout").GetInt32(), Is.EqualTo(30));
        Assert.That(resultDoc.RootElement.GetProperty("version").GetString(), Is.EqualTo("1.0"));
        Assert.That(resultDoc.RootElement.GetProperty("author").GetString(), Is.EqualTo("test"));
    }

    [Test]
    public void MergeToDocument_ShouldReturnJsonDocument()
    {
        // Arrange
        var parentJson = """{"name": "parent"}""";
        var targetJson = """{"age": 25}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        using var result = UtilsJson.MergeToDocument(parentDoc, targetDoc);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.RootElement.GetProperty("name").GetString(), Is.EqualTo("parent"));
        Assert.That(result.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(25));
    }

    [Test]
    public void Merge_ParentIsNotContainer_ShouldThrowException()
    {
        // Arrange
        var parentJson = """42""";
        var targetJson = """{"key": "value"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => UtilsJson.Merge(parentDoc, targetDoc));
        Assert.That(ex.Message, Does.Contain("must be a container type"));
        Assert.That(ex.Message, Does.Contain("Number"));
    }

    [Test]
    public void Merge_TargetIsNotContainer_ShouldThrowException()
    {
        // Arrange
        var parentJson = """{"key": "value"}""";
        var targetJson = """42""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => UtilsJson.Merge(parentDoc, targetDoc));
        Assert.That(ex.Message, Does.Contain("must be a container type"));
        Assert.That(ex.Message, Does.Contain("Number"));
    }

    [Test]
    public void Merge_MismatchedContainerTypes_ShouldThrowException()
    {
        // Arrange
        var parentJson = """{"key": "value"}""";
        var targetJson = """[1, 2, 3]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => UtilsJson.Merge(parentDoc, targetDoc));
        Assert.That(ex.Message, Does.Contain("must be a container type"));
        Assert.That(ex.Message, Does.Contain("Array"));
    }

    [Test]
    public void Merge_ArrayWithMixedTypes_ShouldConcatenateAll()
    {
        // Arrange
        var parentJson = """[1, "string", true]""";
        var targetJson = """[null, {"key": "value"}, [1, 2]]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(6));
        Assert.That(array[0].GetInt32(), Is.EqualTo(1));
        Assert.That(array[1].GetString(), Is.EqualTo("string"));
        Assert.That(array[2].GetBoolean(), Is.True);
        Assert.That(array[3].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(array[4].ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(array[5].ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public void Merge_SinglePropertyOverride_ShouldPreserveOtherProperties()
    {
        // Arrange
        var parentJson = """{"a": 1, "b": 2, "c": 3, "d": 4}""";
        var targetJson = """{"b": 999}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = UtilsJson.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("a").GetInt32(), Is.EqualTo(1));
        Assert.That(resultDoc.RootElement.GetProperty("b").GetInt32(), Is.EqualTo(999)); // overridden
        Assert.That(resultDoc.RootElement.GetProperty("c").GetInt32(), Is.EqualTo(3));
        Assert.That(resultDoc.RootElement.GetProperty("d").GetInt32(), Is.EqualTo(4));
    }
}