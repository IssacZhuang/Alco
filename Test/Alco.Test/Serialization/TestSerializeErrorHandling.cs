using System;
using System.Collections.Generic;

#nullable enable

namespace Alco.Test;

/// <summary>
/// Tests for error handling in serialization to ensure that exceptions in one node
/// don't affect the serialization of other nodes.
/// </summary>
public class TestSerializeErrorHandling
{
    /// <summary>
    /// Test object that throws an exception during serialization.
    /// </summary>
    private class FaultySerializableObject : ISerializable
    {
        public string Name = "";
        public bool ShouldThrow = false;
        public string ErrorMessage = "Test error";

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("name", ref Name);

            if (ShouldThrow)
            {
                throw new InvalidOperationException(ErrorMessage);
            }
        }
    }

    /// <summary>
    /// Test object that contains multiple serializable objects.
    /// </summary>
    private class ContainerObject : ISerializable
    {
        public string ContainerName = "";
        public FaultySerializableObject? Object1;
        public FaultySerializableObject? Object2;
        public FaultySerializableObject? Object3;
        public List<FaultySerializableObject> ObjectList = new();

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            node.BindSerializableOptional("object1", ref Object1, static (SerializeReadNode subNode) =>
            {
                string name = subNode.GetString("name");
                return new FaultySerializableObject { Name = name };
            });
            node.BindSerializableOptional("object2", ref Object2, static (SerializeReadNode subNode) =>
            {
                string name = subNode.GetString("name");
                return new FaultySerializableObject { Name = name };
            });
            node.BindSerializableOptional("object3", ref Object3, static (SerializeReadNode subNode) =>
            {
                string name = subNode.GetString("name");
                return new FaultySerializableObject { Name = name };
            });
            node.BindCollectionSerializable("objectList", ObjectList, static (SerializeReadNode subNode) =>
            {
                string name = subNode.GetString("name");
                return new FaultySerializableObject { Name = name };
            });
        }
    }

    /// <summary>
    /// Test error handler that collects error messages.
    /// </summary>
    private class ErrorCollector
    {
        public List<string> Errors { get; } = new();

        public void OnError(string error)
        {
            Errors.Add(error);
        }
    }

    [Test]
    public void TestSerializeWithSingleObjectError()
    {
        // Arrange
        var errorCollector = new ErrorCollector();
        var container = new ContainerObject
        {
            ContainerName = "TestContainer",
            Object1 = new FaultySerializableObject { Name = "Object1", ShouldThrow = false },
            Object2 = new FaultySerializableObject { Name = "Object2", ShouldThrow = true, ErrorMessage = "Object2 error" },
            Object3 = new FaultySerializableObject { Name = "Object3", ShouldThrow = false }
        };

        // Act - Serialize using BinaryParser
        ReadOnlyMemory<byte> serializedData = BinaryParser.Encode(container, errorCollector.OnError);

        // Assert - One error should be collected
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(1));
        Assert.That(errorCollector.Errors[0], Does.Contain("Failed to bind optional serializable 'object2'"));
        Assert.That(errorCollector.Errors[0], Does.Contain("Object2 error"));

        // Act - Deserialize using BinaryParser
        errorCollector.Errors.Clear();
        var deserializedContainer = BinaryParser.Decode<ContainerObject>(serializedData, errorCollector.OnError);

        // Assert - Container name should be preserved
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("TestContainer"));
        // Object1 and Object3 should be deserialized successfully
        Assert.That(deserializedContainer.Object1?.Name, Is.EqualTo("Object1"));
        Assert.That(deserializedContainer.Object3?.Name, Is.EqualTo("Object3"));
        // Object2 should be null due to serialization error, but no deserialization errors
        Assert.That(deserializedContainer.Object2, Is.Null);
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestSerializeWithListItemErrors()
    {
        // Arrange
        var errorCollector = new ErrorCollector();
        var container = new ContainerObject
        {
            ContainerName = "TestContainer",
            ObjectList = new List<FaultySerializableObject>
            {
                new() { Name = "Item0", ShouldThrow = false },
                new() { Name = "Item1", ShouldThrow = true, ErrorMessage = "Item1 error" },
                new() { Name = "Item2", ShouldThrow = false },
                new() { Name = "Item3", ShouldThrow = true, ErrorMessage = "Item3 error" },
                new() { Name = "Item4", ShouldThrow = false }
            }
        };

        // Act - Serialize using BinaryParser
        ReadOnlyMemory<byte> serializedData = BinaryParser.Encode(container, errorCollector.OnError);

        // Assert - Two errors should be collected
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(2));
        Assert.That(errorCollector.Errors[0], Does.Contain("Failed to bind serializable list item at index 1 for key 'objectList'"));
        Assert.That(errorCollector.Errors[0], Does.Contain("Item1 error"));
        Assert.That(errorCollector.Errors[1], Does.Contain("Failed to bind serializable list item at index 3 for key 'objectList'"));
        Assert.That(errorCollector.Errors[1], Does.Contain("Item3 error"));

        // Act - Deserialize using BinaryParser
        errorCollector.Errors.Clear();
        var deserializedContainer = BinaryParser.Decode<ContainerObject>(serializedData, errorCollector.OnError);

        // Assert - Container name should be preserved
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("TestContainer"));
        // Only successfully serialized items should be in the list (Item0, Item2, Item4)
        Assert.That(deserializedContainer.ObjectList.Count, Is.EqualTo(3));
        Assert.That(deserializedContainer.ObjectList[0].Name, Is.EqualTo("Item0"));
        Assert.That(deserializedContainer.ObjectList[1].Name, Is.EqualTo("Item2"));
        Assert.That(deserializedContainer.ObjectList[2].Name, Is.EqualTo("Item4"));
        // No deserialization errors
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestDeserializeWithSingleObjectError()
    {
        // Arrange - Create a valid serialized object first
        var container = new ContainerObject
        {
            ContainerName = "TestContainer",
            Object1 = new FaultySerializableObject { Name = "Object1" },
            Object2 = new FaultySerializableObject { Name = "Object2" },
            Object3 = new FaultySerializableObject { Name = "Object3" }
        };

        ReadOnlyMemory<byte> serializedData = BinaryParser.Encode(container);
        var table = BinaryParser.DecodeTable(serializedData);

        // Act - Deserialize with one object that will throw during deserialization
        var errorCollector = new ErrorCollector();
        var referenceContext = new ReferenceContext();
        var readNode = new BinarySerializeReadNode(referenceContext, table, errorCollector.OnError);
        var deserializedContainer = new ContainerObject
        {
            Object2 = new FaultySerializableObject { ShouldThrow = true, ErrorMessage = "Deserialization error" }
        };
        deserializedContainer.OnSerialize(readNode, SerializeMode.Load);

        // Assert - One error should be collected during deserialization
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(1));
        Assert.That(errorCollector.Errors[0], Does.Contain("Failed to bind optional serializable 'object2'"));
        Assert.That(errorCollector.Errors[0], Does.Contain("Deserialization error"));

        // Container name and other objects should still be deserialized successfully
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("TestContainer"));
        Assert.That(deserializedContainer.Object1?.Name, Is.EqualTo("Object1"));
        Assert.That(deserializedContainer.Object3?.Name, Is.EqualTo("Object3"));
    }

    [Test]
    public void TestDeserializeWithListItemErrors()
    {
        // Arrange - Create a valid serialized list first using BinaryParser
        var container = new ContainerObject
        {
            ContainerName = "TestContainer",
            ObjectList = new List<FaultySerializableObject>
            {
                new() { Name = "Item0" },
                new() { Name = "Item1" },
                new() { Name = "Item2" },
                new() { Name = "Item3" },
                new() { Name = "Item4" }
            }
        };

        ReadOnlyMemory<byte> serializedData = BinaryParser.Encode(container);
        var table = BinaryParser.DecodeTable(serializedData);

        // Act - Deserialize with a custom factory that throws for certain items
        // Note: We still need to use BinarySerializeReadNode here to test the custom factory error handling
        var errorCollector = new ErrorCollector();
        var referenceContext = new ReferenceContext();
        var readNode = new BinarySerializeReadNode(referenceContext, table, errorCollector.OnError);
        var deserializedContainer = new ContainerObject();

        // First bind the container name, then manually bind the list with a custom factory
        readNode.BindString("containerName", ref deserializedContainer.ContainerName);
        readNode.BindCollectionSerializable("objectList", deserializedContainer.ObjectList, (SerializeReadNode subNode) =>
        {
            string name = subNode.GetString("name");
            if (name == "Item1")
            {
                //it will break the deserialization
                throw new InvalidOperationException($"Factory error for {name}");
            }
            return new FaultySerializableObject { Name = name };
        });

        // Assert - Two errors should be collected during deserialization
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(1));
        Assert.That(errorCollector.Errors[0], Does.Contain("Failed to bind serializable list item at index 1 for key 'objectList'"));
        Assert.That(errorCollector.Errors[0], Does.Contain("Factory error for Item1"));

        // Container name should be deserialized successfully
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("TestContainer"));
        // All items except the faulty one should be deserialized (Item0, Item2, Item3, Item4)
        Assert.That(deserializedContainer.ObjectList.Count, Is.EqualTo(4));
        Assert.That(deserializedContainer.ObjectList[0].Name, Is.EqualTo("Item0"));
        Assert.That(deserializedContainer.ObjectList[1].Name, Is.EqualTo("Item2"));
        Assert.That(deserializedContainer.ObjectList[2].Name, Is.EqualTo("Item3"));
        Assert.That(deserializedContainer.ObjectList[3].Name, Is.EqualTo("Item4"));
    }

    [Test]
    public void TestMultipleErrorsFromDifferentSources()
    {
        // Arrange
        var errorCollector = new ErrorCollector();
        var container = new ContainerObject
        {
            ContainerName = "TestContainer",
            Object1 = new FaultySerializableObject { Name = "Object1", ShouldThrow = true, ErrorMessage = "Object1 error" },
            Object2 = new FaultySerializableObject { Name = "Object2", ShouldThrow = false },
            Object3 = new FaultySerializableObject { Name = "Object3", ShouldThrow = true, ErrorMessage = "Object3 error" },
            ObjectList = new List<FaultySerializableObject>
            {
                new() { Name = "ListItem0", ShouldThrow = false },
                new() { Name = "ListItem1", ShouldThrow = true, ErrorMessage = "ListItem1 error" }
            }
        };

        // Act - Serialize using BinaryParser
        ReadOnlyMemory<byte> serializedData = BinaryParser.Encode(container, errorCollector.OnError);

        // Assert - Three errors should be collected (2 from objects, 1 from list)
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(3));

        var object1Error = errorCollector.Errors.Find(e => e.Contains("object1"));
        var object3Error = errorCollector.Errors.Find(e => e.Contains("object3"));
        var listError = errorCollector.Errors.Find(e => e.Contains("objectList"));

        Assert.That(object1Error, Is.Not.Null);
        Assert.That(object1Error, Does.Contain("Object1 error"));
        Assert.That(object3Error, Is.Not.Null);
        Assert.That(object3Error, Does.Contain("Object3 error"));
        Assert.That(listError, Is.Not.Null);
        Assert.That(listError, Does.Contain("ListItem1 error"));

        // Act - Deserialize using BinaryParser
        errorCollector.Errors.Clear();
        var deserializedContainer = BinaryParser.Decode<ContainerObject>(serializedData, errorCollector.OnError);

        // Assert - Successful serialization/deserialization for non-faulty objects
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("TestContainer"));
        Assert.That(deserializedContainer.Object1, Is.Null); // Failed to serialize
        Assert.That(deserializedContainer.Object2?.Name, Is.EqualTo("Object2")); // Success
        Assert.That(deserializedContainer.Object3, Is.Null); // Failed to serialize
        Assert.That(deserializedContainer.ObjectList.Count, Is.EqualTo(1)); // Only successful item
        Assert.That(deserializedContainer.ObjectList[0].Name, Is.EqualTo("ListItem0"));
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(0)); // No deserialization errors
    }

    [Test]
    public void TestErrorHandlingWithDefaultErrorHandler()
    {
        // Arrange - No custom error handler, should use default (Log.Error)
        var container = new ContainerObject
        {
            ContainerName = "TestContainer",
            Object1 = new FaultySerializableObject { Name = "Object1", ShouldThrow = true, ErrorMessage = "Test error" }
        };

        // Act - This should not throw, even without custom error handler
        ReadOnlyMemory<byte> serializedData = ReadOnlyMemory<byte>.Empty;
        Assert.DoesNotThrow(() => serializedData = BinaryParser.Encode(container)); // No error handler

        // Deserialize should also not throw
        ContainerObject? deserializedContainer = null;
        Assert.DoesNotThrow(() => deserializedContainer = BinaryParser.Decode<ContainerObject>(serializedData)); // No error handler

        // Basic functionality should still work
        Assert.That(deserializedContainer!.ContainerName, Is.EqualTo("TestContainer"));
        Assert.That(deserializedContainer.Object1, Is.Null); // Failed to serialize
    }
}