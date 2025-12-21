using System;
using System.Text.Json;
using NUnit.Framework;

namespace Alco.Engine.Test;

[TestFixture]
public class TestJsonUtility
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
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
    public void Merge_Arrays_ShouldReplaceByDefault()
    {
        // Arrange
        var parentJson = """[1, 2, 3]""";
        var targetJson = """[4, 5, 6]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(3));
        Assert.That(array[0].GetInt32(), Is.EqualTo(4));
        Assert.That(array[1].GetInt32(), Is.EqualTo(5));
        Assert.That(array[2].GetInt32(), Is.EqualTo(6));
    }

    [Test]
    public void Merge_ObjectsWithArrays_ShouldReplaceArraysByDefault()
    {
        // Arrange
        var parentJson = """{"tags": ["tag1", "tag2"], "name": "parent"}""";
        var targetJson = """{"tags": ["tag3", "tag4"], "description": "target"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var tagsArray = resultDoc.RootElement.GetProperty("tags").EnumerateArray().ToArray();
        Assert.That(tagsArray.Length, Is.EqualTo(2));
        Assert.That(tagsArray[0].GetString(), Is.EqualTo("tag3"));
        Assert.That(tagsArray[1].GetString(), Is.EqualTo("tag4"));
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
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
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var configElement = resultDoc.RootElement.GetProperty("config");
        var databaseElement = configElement.GetProperty("database");

        Assert.That(databaseElement.GetProperty("host").GetString(), Is.EqualTo("localhost"));
        Assert.That(databaseElement.GetProperty("port").GetInt32(), Is.EqualTo(3306)); // target overrides
        Assert.That(databaseElement.GetProperty("name").GetString(), Is.EqualTo("mydb"));

        var featuresArray = configElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.That(featuresArray.Length, Is.EqualTo(1));
        Assert.That(featuresArray[0].GetString(), Is.EqualTo("cache"));

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
        using var result = JsonUtility.MergeToDocument(parentDoc, targetDoc);

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
        var ex = Assert.Throws<InvalidOperationException>(() => JsonUtility.Merge(parentDoc, targetDoc));
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
        var ex = Assert.Throws<InvalidOperationException>(() => JsonUtility.Merge(parentDoc, targetDoc));
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
        var ex = Assert.Throws<InvalidOperationException>(() => JsonUtility.Merge(parentDoc, targetDoc));
        Assert.That(ex.Message, Does.Contain("must be a container type"));
        Assert.That(ex.Message, Does.Contain("Array"));
    }

    [Test]
    public void Merge_ArrayWithMixedTypes_ShouldReplaceByDefault()
    {
        // Arrange
        var parentJson = """[1, "string", true]""";
        var targetJson = """[null, {"key": "value"}, [1, 2]]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(3));
        Assert.That(array[0].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(array[1].ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(array[2].ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public void Merge_SinglePropertyOverride_ShouldPreserveOtherProperties()
    {
        // Arrange
        var parentJson = """{"name": "John", "age": 30, "city": "Boston"}""";
        var targetJson = """{"age": 25}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("John"));
        Assert.That(resultDoc.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(25));
        Assert.That(resultDoc.RootElement.GetProperty("city").GetString(), Is.EqualTo("Boston"));
    }

    [Test]
    public void Merge_InheritFalse_ShouldCompletelyOverrideParent()
    {
        // Arrange
        var parentJson = """{"name": "parent", "age": 30, "city": "Boston"}""";
        var targetJson = """{"$inherit": false, "name": "child", "version": "1.0"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("child"));
        Assert.That(resultDoc.RootElement.GetProperty("version").GetString(), Is.EqualTo("1.0"));
        Assert.That(resultDoc.RootElement.TryGetProperty("age", out _), Is.False);
        Assert.That(resultDoc.RootElement.TryGetProperty("city", out _), Is.False);
        Assert.That(resultDoc.RootElement.TryGetProperty("$inherit", out _), Is.False);
    }

    [Test]
    public void Merge_InheritFalse_NestedObject_ShouldOverrideParentNested()
    {
        // Arrange
        var parentJson = """
        {
            "config": {
                "database": {
                    "host": "localhost",
                    "port": 5432,
                    "timeout": 30
                },
                "cache": true
            },
            "version": "1.0"
        }
        """;

        var targetJson = """
        {
            "config": {
                "database": {
                    "$inherit": false,
                    "host": "remote",
                    "ssl": true
                }
            }
        }
        """;

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var config = resultDoc.RootElement.GetProperty("config");
        var database = config.GetProperty("database");

        Assert.That(database.GetProperty("host").GetString(), Is.EqualTo("remote"));
        Assert.That(database.GetProperty("ssl").GetBoolean(), Is.True);
        Assert.That(database.TryGetProperty("port", out _), Is.False);
        Assert.That(database.TryGetProperty("timeout", out _), Is.False);
        Assert.That(database.TryGetProperty("$inherit", out _), Is.False);

        // Other properties should still be merged normally
        Assert.That(config.GetProperty("cache").GetBoolean(), Is.True);
        Assert.That(resultDoc.RootElement.GetProperty("version").GetString(), Is.EqualTo("1.0"));
    }

    [Test]
    public void Merge_InheritTrue_ShouldMergeNormally()
    {
        // Arrange
        var parentJson = """{"name": "parent", "age": 30}""";
        var targetJson = """{"$inherit": true, "name": "child", "version": "1.0"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("child"));
        Assert.That(resultDoc.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(30));
        Assert.That(resultDoc.RootElement.GetProperty("version").GetString(), Is.EqualTo("1.0"));
        Assert.That(resultDoc.RootElement.TryGetProperty("$inherit", out _), Is.False);
    }

    [Test]
    public void Merge_InheritString_ShouldMergeNormally()
    {
        // Arrange
        var parentJson = """{"name": "parent", "age": 30}""";
        var targetJson = """{"$inherit": "some_value", "name": "child", "version": "1.0"}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("child"));
        Assert.That(resultDoc.RootElement.GetProperty("age").GetInt32(), Is.EqualTo(30));
        Assert.That(resultDoc.RootElement.GetProperty("version").GetString(), Is.EqualTo("1.0"));
        Assert.That(resultDoc.RootElement.TryGetProperty("$inherit", out _), Is.False);
    }

    [Test]
    public void Merge_InheritFalse_EmptyTargetObject_ShouldResultInEmptyObject()
    {
        // Arrange
        var parentJson = """{"name": "parent", "age": 30, "city": "Boston"}""";
        var targetJson = """{"$inherit": false}""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        Assert.That(resultDoc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(resultDoc.RootElement.EnumerateObject().Count(), Is.EqualTo(0));
    }

    [Test]
    public void Merge_InheritFalse_ComplexTarget_ShouldOnlyContainTargetProperties()
    {
        // Arrange
        var parentJson = """
        {
            "database": {
                "host": "localhost",
                "port": 5432
            },
            "cache": {
                "enabled": true,
                "ttl": 3600
            },
            "features": ["auth", "logging"]
        }
        """;

        var targetJson = """
        {
            "$inherit": false,
            "database": {
                "host": "remote",
                "ssl": true
            },
            "newFeature": "enabled"
        }
        """;

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var database = resultDoc.RootElement.GetProperty("database");
        Assert.That(database.GetProperty("host").GetString(), Is.EqualTo("remote"));
        Assert.That(database.GetProperty("ssl").GetBoolean(), Is.True);

        Assert.That(resultDoc.RootElement.GetProperty("newFeature").GetString(), Is.EqualTo("enabled"));

        // Parent properties should not exist
        Assert.That(resultDoc.RootElement.TryGetProperty("cache", out _), Is.False);
        Assert.That(resultDoc.RootElement.TryGetProperty("features", out _), Is.False);
        Assert.That(resultDoc.RootElement.TryGetProperty("$inherit", out _), Is.False);
    }

    [Test]
    public void Merge_ArrayInheritFalse_ShouldReplaceParentArray()
    {
        // Arrange
        var parentJson = """["a", "b", "c"]""";
        var targetJson = """[{"$inherit": false}, "x", "y"]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(2));
        Assert.That(array[0].GetString(), Is.EqualTo("x"));
        Assert.That(array[1].GetString(), Is.EqualTo("y"));
    }

    [Test]
    public void Merge_ArrayInheritPrepend_ShouldPrependTargetToParent()
    {
        // Arrange
        var parentJson = """["a", "b", "c"]""";
        var targetJson = """[{"$inherit": "prepend"}, "x", "y"]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(5));
        Assert.That(array[0].GetString(), Is.EqualTo("x"));
        Assert.That(array[1].GetString(), Is.EqualTo("y"));
        Assert.That(array[2].GetString(), Is.EqualTo("a"));
        Assert.That(array[3].GetString(), Is.EqualTo("b"));
        Assert.That(array[4].GetString(), Is.EqualTo("c"));
    }

    [Test]
    public void Merge_ArrayInheritAppend_ShouldAppendTargetToParent()
    {
        // Arrange
        var parentJson = """["a", "b", "c"]""";
        var targetJson = """[{"$inherit": "append"}, "x", "y"]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(5));
        Assert.That(array[0].GetString(), Is.EqualTo("a"));
        Assert.That(array[1].GetString(), Is.EqualTo("b"));
        Assert.That(array[2].GetString(), Is.EqualTo("c"));
        Assert.That(array[3].GetString(), Is.EqualTo("x"));
        Assert.That(array[4].GetString(), Is.EqualTo("y"));
    }

    [Test]
    public void Merge_ArrayWithoutInheritControl_ShouldUseDefaultBehavior()
    {
        // Arrange
        var parentJson = """["a", "b"]""";
        var targetJson = """["x", "y"]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(2));
        Assert.That(array[0].GetString(), Is.EqualTo("x"));
        Assert.That(array[1].GetString(), Is.EqualTo("y"));
    }

    [Test]
    public void Merge_ArrayInheritFalse_EmptyTargetArray_ShouldResultInEmptyArray()
    {
        // Arrange
        var parentJson = """["a", "b", "c"]""";
        var targetJson = """[{"$inherit": false}]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(0));
    }

    [Test]
    public void Merge_ArrayInheritInObject_ShouldControlNestedArrays()
    {
        // Arrange
        var parentJson = """
        {
            "tags": ["tag1", "tag2"],
            "items": ["item1", "item2"]
        }
        """;

        var targetJson = """
        {
            "tags": [{"$inherit": "prepend"}, "new-tag"],
            "items": [{"$inherit": false}, "new-item"]
        }
        """;

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var tagsArray = resultDoc.RootElement.GetProperty("tags").EnumerateArray().ToArray();
        Assert.That(tagsArray.Length, Is.EqualTo(3));
        Assert.That(tagsArray[0].GetString(), Is.EqualTo("new-tag"));
        Assert.That(tagsArray[1].GetString(), Is.EqualTo("tag1"));
        Assert.That(tagsArray[2].GetString(), Is.EqualTo("tag2"));

        var itemsArray = resultDoc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.That(itemsArray.Length, Is.EqualTo(1));
        Assert.That(itemsArray[0].GetString(), Is.EqualTo("new-item"));
    }

    [Test]
    public void Merge_ArrayInheritWithMixedTypes_ShouldHandleCorrectly()
    {
        // Arrange
        var parentJson = """[1, "string", true]""";
        var targetJson = """[{"$inherit": "prepend"}, {"key": "value"}, null]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(5));
        Assert.That(array[0].ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(array[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(array[2].GetInt32(), Is.EqualTo(1));
        Assert.That(array[3].GetString(), Is.EqualTo("string"));
        Assert.That(array[4].GetBoolean(), Is.True);
    }

    [Test]
    public void Merge_ArrayInheritInvalidMode_ShouldFallbackToDefault()
    {
        // Arrange
        var parentJson = """["a", "b"]""";
        var targetJson = """[{"$inherit": "invalid_mode"}, "x", "y"]""";

        using var parentDoc = JsonDocument.Parse(parentJson);
        using var targetDoc = JsonDocument.Parse(targetJson);

        // Act
        var result = JsonUtility.Merge(parentDoc, targetDoc);
        using var resultDoc = JsonDocument.Parse(result);

        // Assert - should fall back to default behavior (replace with all target elements including control object)
        var array = resultDoc.RootElement.EnumerateArray().ToArray();
        Assert.That(array.Length, Is.EqualTo(3)); // all target elements
        Assert.That(array[0].ValueKind, Is.EqualTo(JsonValueKind.Object)); // control object
        Assert.That(array[1].GetString(), Is.EqualTo("x"));
        Assert.That(array[2].GetString(), Is.EqualTo("y"));
    }
}