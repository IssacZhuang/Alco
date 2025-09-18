using System;
using System.Collections.Generic;

#nullable enable

namespace Alco.Test;

/// <summary>
/// Tests for BindReference functionality in serialization to ensure that object references
/// are properly handled during serialization, deserialization, and post-load phases.
/// 
/// The reference system works in three phases:
/// 1. Save (Serialization): Objects implementing IReferenceable are assigned unique IDs and stored normally.
///    BindReference stores the ID of referenced objects.
/// 2. Load (Deserialization): Objects are recreated and registered in the reference context by their IDs.
///    BindReference does nothing during this phase.
/// 3. PostLoad: BindReference resolves the stored IDs to actual object instances from the reference context.
/// 
/// Key concepts tested:
/// - Basic reference serialization/deserialization
/// - Null reference handling
/// - Shared references (multiple variables pointing to the same object)
/// - Circular references (objects referencing each other)
/// - Nested references (objects with child references)
/// - Error handling for invalid reference IDs
/// - Complex scenarios with multiple types of references
/// - References with BindSerializable serialized objects
/// - References with BindCollectionSerializable serialized collections
/// - Mixed serialization methods with references
/// </summary>
public class TestBindReference
{
    /// <summary>
    /// Test referenceable object that implements IReferenceable.
    /// </summary>
    private class ReferenceableObject : IReferenceable
    {
        public string Name = "";
        public int Value = 0;
        public ReferenceableObject? ChildReference;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("name", ref Name);
            node.BindValue("value", ref Value);
            node.BindReference("childReference", ref ChildReference);
        }
    }

    /// <summary>
    /// Container object that holds multiple references to test various scenarios.
    /// The key insight is that objects must first be serialized normally (to register them in the reference context)
    /// before they can be referenced by BindReference.
    /// </summary>
    private class ReferenceContainer : ISerializable
    {
        public string ContainerName = "";
        public ReferenceableObject? OriginalObject;  // The actual object being referenced
        public ReferenceableObject? SecondObject;    // Another object for testing
        public ReferenceableObject? FirstReference;  // Reference to OriginalObject
        public ReferenceableObject? SecondReference; // Reference to SecondObject
        public ReferenceableObject? SharedReference; // Another reference to OriginalObject

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            
            // First, serialize the actual objects (this registers them in the reference context)
            node.BindSerializableOptional("originalObject", ref OriginalObject, static (SerializeReadNode subNode) =>
            {
                return new ReferenceableObject();
            });
            node.BindSerializableOptional("secondObject", ref SecondObject, static (SerializeReadNode subNode) =>
            {
                return new ReferenceableObject();
            });
            
            // Then, serialize the references to these objects
            node.BindReference("firstReference", ref FirstReference);
            node.BindReference("secondReference", ref SecondReference);
            node.BindReference("sharedReference", ref SharedReference);
        }
    }

    /// <summary>
    /// Test object that creates circular references.
    /// For circular references to work, we need to first serialize the object normally,
    /// then handle the circular reference in a second pass.
    /// </summary>
    private class CircularReferenceContainer : ISerializable
    {
        public CircularReferenceObject? ObjectA;
        public CircularReferenceObject? ObjectB;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            // First serialize the objects normally (this registers them)
            node.BindSerializableOptional("objectA", ref ObjectA, static (SerializeReadNode subNode) =>
            {
                return new CircularReferenceObject();
            });
            node.BindSerializableOptional("objectB", ref ObjectB, static (SerializeReadNode subNode) =>
            {
                return new CircularReferenceObject();
            });
        }
    }

    /// <summary>
    /// Test object that creates circular references.
    /// </summary>
    private class CircularReferenceObject : IReferenceable
    {
        public string Name = "";
        public CircularReferenceObject? Partner;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("name", ref Name);
            node.BindReference("partner", ref Partner);
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
    public void TestBasicReferenceSerializationAndDeserialization()
    {
        // Arrange
        var referencedObject = new ReferenceableObject
        {
            Name = "Referenced Object",
            Value = 42
        };

        var container = new ReferenceContainer
        {
            ContainerName = "Test Container",
            OriginalObject = referencedObject,
            FirstReference = referencedObject  // Reference to the same object
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<ReferenceContainer>(serializedData);

        // Assert
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Test Container"));
        Assert.That(deserializedContainer.OriginalObject, Is.Not.Null);
        Assert.That(deserializedContainer.FirstReference, Is.Not.Null);
        Assert.That(deserializedContainer.OriginalObject!.Name, Is.EqualTo("Referenced Object"));
        Assert.That(deserializedContainer.OriginalObject.Value, Is.EqualTo(42));
        
        // Most importantly, the reference should point to the same object instance
        Assert.That(ReferenceEquals(deserializedContainer.OriginalObject, deserializedContainer.FirstReference), Is.True);
        
        Assert.That(deserializedContainer.SecondReference, Is.Null);
        Assert.That(deserializedContainer.SharedReference, Is.Null);
    }

    [Test]
    public void TestNullReferenceHandling()
    {
        // Arrange
        var container = new ReferenceContainer
        {
            ContainerName = "Test Container",
            OriginalObject = null,
            SecondObject = null,
            FirstReference = null,
            SecondReference = null,
            SharedReference = null
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<ReferenceContainer>(serializedData);

        // Assert - All references should remain null
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Test Container"));
        Assert.That(deserializedContainer.OriginalObject, Is.Null);
        Assert.That(deserializedContainer.SecondObject, Is.Null);
        Assert.That(deserializedContainer.FirstReference, Is.Null);
        Assert.That(deserializedContainer.SecondReference, Is.Null);
        Assert.That(deserializedContainer.SharedReference, Is.Null);
    }

    [Test]
    public void TestSharedReferencesSameObject()
    {
        // Arrange - Create a shared object that is referenced by multiple properties
        var sharedObject = new ReferenceableObject
        {
            Name = "Shared Object",
            Value = 100
        };

        var container = new ReferenceContainer
        {
            ContainerName = "Shared Reference Test",
            OriginalObject = sharedObject,
            FirstReference = sharedObject,
            SharedReference = sharedObject // Same object instance
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<ReferenceContainer>(serializedData);

        // Assert - All references should point to the same deserialized object
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Shared Reference Test"));
        Assert.That(deserializedContainer.OriginalObject, Is.Not.Null);
        Assert.That(deserializedContainer.FirstReference, Is.Not.Null);
        Assert.That(deserializedContainer.SharedReference, Is.Not.Null);
        
        // Verify object identity - should be the same object instance
        Assert.That(ReferenceEquals(deserializedContainer.OriginalObject, deserializedContainer.FirstReference), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.OriginalObject, deserializedContainer.SharedReference), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.FirstReference, deserializedContainer.SharedReference), Is.True);
        
        // Verify content
        Assert.That(deserializedContainer.OriginalObject!.Name, Is.EqualTo("Shared Object"));
        Assert.That(deserializedContainer.OriginalObject.Value, Is.EqualTo(100));
    }

    [Test]
    public void TestCircularReferences()
    {
        // Arrange - Create circular references
        var objectA = new CircularReferenceObject { Name = "Object A" };
        var objectB = new CircularReferenceObject { Name = "Object B" };
        
        // Create circular reference
        objectA.Partner = objectB;
        objectB.Partner = objectA;

        var container = new CircularReferenceContainer
        {
            ObjectA = objectA,
            ObjectB = objectB
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<CircularReferenceContainer>(serializedData);

        // Assert - Circular references should be preserved
        Assert.That(deserializedContainer.ObjectA, Is.Not.Null);
        Assert.That(deserializedContainer.ObjectB, Is.Not.Null);
        Assert.That(deserializedContainer.ObjectA!.Name, Is.EqualTo("Object A"));
        Assert.That(deserializedContainer.ObjectB!.Name, Is.EqualTo("Object B"));
        Assert.That(deserializedContainer.ObjectA.Partner, Is.Not.Null);
        Assert.That(deserializedContainer.ObjectB.Partner, Is.Not.Null);
        
        // Verify circular reference - Partners should point to each other
        Assert.That(ReferenceEquals(deserializedContainer.ObjectA, deserializedContainer.ObjectB.Partner), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.ObjectB, deserializedContainer.ObjectA.Partner), Is.True);
    }

    [Test]
    public void TestNestedReferences()
    {
        // Arrange - Create nested references (child references)
        var childObject = new ReferenceableObject
        {
            Name = "Child Object",
            Value = 10
        };

        var parentObject = new ReferenceableObject
        {
            Name = "Parent Object",
            Value = 20,
            ChildReference = childObject
        };

        var container = new ReferenceContainer
        {
            ContainerName = "Nested Reference Test",
            OriginalObject = parentObject,
            SecondObject = childObject,  // Also serialize the child object so it can be referenced
            FirstReference = parentObject
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<ReferenceContainer>(serializedData);

        // Assert - Nested references should be preserved
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Nested Reference Test"));
        Assert.That(deserializedContainer.OriginalObject, Is.Not.Null);
        Assert.That(deserializedContainer.SecondObject, Is.Not.Null);
        Assert.That(deserializedContainer.FirstReference, Is.Not.Null);
        
        // Verify the object structure
        Assert.That(deserializedContainer.OriginalObject!.Name, Is.EqualTo("Parent Object"));
        Assert.That(deserializedContainer.OriginalObject.Value, Is.EqualTo(20));
        Assert.That(deserializedContainer.SecondObject!.Name, Is.EqualTo("Child Object"));
        Assert.That(deserializedContainer.SecondObject.Value, Is.EqualTo(10));
        
        // Verify references point to the correct objects
        Assert.That(ReferenceEquals(deserializedContainer.OriginalObject, deserializedContainer.FirstReference), Is.True);
        Assert.That(deserializedContainer.FirstReference!.ChildReference, Is.Not.Null);
        Assert.That(ReferenceEquals(deserializedContainer.SecondObject, deserializedContainer.FirstReference.ChildReference), Is.True);
    }

    [Test]
    public void TestReferenceErrorHandling()
    {
        // Arrange - Create a manually corrupted reference scenario
        var errorCollector = new ErrorCollector();
        var referenceContext = new ReferenceContext();
        
        // Create a binary table with invalid reference IDs
        var table = new BinaryTable();
        table.Add("containerName", "Error Test");
        table.Add("firstReference", (uint)999); // Invalid reference ID
        table.Add("secondReference", (uint)888); // Another invalid reference ID
        table.Add("sharedReference", (uint)777); // Yet another invalid reference ID
        
        // Act - Try to deserialize with the corrupted data
        var container = new ReferenceContainer();
        var readNode = new BinarySerializeReadNode(referenceContext, table, errorCollector.OnError);
        container.OnSerialize(readNode, SerializeMode.Load);
        
        // Post-load phase where references are resolved
        var postLoadNode = new BinaryPostLoadSerializeNode(referenceContext, table, errorCollector.OnError);
        container.OnSerialize(postLoadNode, SerializeMode.PostLoad);

        // Assert - Errors should be collected for each invalid reference
        Assert.That(errorCollector.Errors.Count, Is.EqualTo(3));
        Assert.That(errorCollector.Errors[0], Does.Contain("Failed to resolve reference 'firstReference': 999"));
        Assert.That(errorCollector.Errors[1], Does.Contain("Failed to resolve reference 'secondReference': 888"));
        Assert.That(errorCollector.Errors[2], Does.Contain("Failed to resolve reference 'sharedReference': 777"));
        
        // All references should remain null
        Assert.That(container.FirstReference, Is.Null);
        Assert.That(container.SecondReference, Is.Null);
        Assert.That(container.SharedReference, Is.Null);
        Assert.That(container.ContainerName, Is.EqualTo("Error Test")); // Other fields should still work
    }

    [Test]
    public void TestComplexReferenceScenario()
    {
        // Arrange - Create a complex scenario with multiple shared and nested references
        // We need a special container for this complex test
        var complexContainer = new ComplexReferenceContainer();
        
        var baseObject = new ReferenceableObject
        {
            Name = "Base Object",
            Value = 1
        };

        var derivedObject1 = new ReferenceableObject
        {
            Name = "Derived Object 1",
            Value = 2,
            ChildReference = baseObject
        };

        var derivedObject2 = new ReferenceableObject
        {
            Name = "Derived Object 2", 
            Value = 3,
            ChildReference = baseObject // Shared reference to base object
        };

        complexContainer.ContainerName = "Complex Reference Test";
        complexContainer.BaseObject = baseObject;
        complexContainer.DerivedObject1 = derivedObject1;
        complexContainer.DerivedObject2 = derivedObject2;
        complexContainer.BaseReference = baseObject;  // Direct reference to base object
        complexContainer.Derived1Reference = derivedObject1;
        complexContainer.Derived2Reference = derivedObject2;

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(complexContainer);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<ComplexReferenceContainer>(serializedData);

        // Assert - All references should be correctly preserved and shared
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Complex Reference Test"));
        
        // Verify all objects are present
        Assert.That(deserializedContainer.BaseObject, Is.Not.Null);
        Assert.That(deserializedContainer.DerivedObject1, Is.Not.Null);
        Assert.That(deserializedContainer.DerivedObject2, Is.Not.Null);
        Assert.That(deserializedContainer.BaseReference, Is.Not.Null);
        Assert.That(deserializedContainer.Derived1Reference, Is.Not.Null);
        Assert.That(deserializedContainer.Derived2Reference, Is.Not.Null);
        
        // Verify object content
        Assert.That(deserializedContainer.BaseObject!.Name, Is.EqualTo("Base Object"));
        Assert.That(deserializedContainer.DerivedObject1!.Name, Is.EqualTo("Derived Object 1"));
        Assert.That(deserializedContainer.DerivedObject2!.Name, Is.EqualTo("Derived Object 2"));
        
        // Verify all references point to the same object instances
        Assert.That(ReferenceEquals(deserializedContainer.BaseObject, deserializedContainer.BaseReference), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.DerivedObject1, deserializedContainer.Derived1Reference), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.DerivedObject2, deserializedContainer.Derived2Reference), Is.True);
        
        // Verify shared child references
        Assert.That(deserializedContainer.DerivedObject1.ChildReference, Is.Not.Null);
        Assert.That(deserializedContainer.DerivedObject2.ChildReference, Is.Not.Null);
        Assert.That(ReferenceEquals(deserializedContainer.BaseObject, deserializedContainer.DerivedObject1.ChildReference), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.BaseObject, deserializedContainer.DerivedObject2.ChildReference), Is.True);
    }

    /// <summary>
    /// Container for complex reference scenarios.
    /// </summary>
    private class ComplexReferenceContainer : ISerializable
    {
        public string ContainerName = "";
        public ReferenceableObject? BaseObject;
        public ReferenceableObject? DerivedObject1;
        public ReferenceableObject? DerivedObject2;
        public ReferenceableObject? BaseReference;
        public ReferenceableObject? Derived1Reference;
        public ReferenceableObject? Derived2Reference;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            
            // First serialize the actual objects (this registers them)
            node.BindSerializableOptional("baseObject", ref BaseObject, static (SerializeReadNode subNode) =>
            {
                return new ReferenceableObject();
            });
            node.BindSerializableOptional("derivedObject1", ref DerivedObject1, static (SerializeReadNode subNode) =>
            {
                return new ReferenceableObject();
            });
            node.BindSerializableOptional("derivedObject2", ref DerivedObject2, static (SerializeReadNode subNode) =>
            {
                return new ReferenceableObject();
            });
            
            // Then serialize the references
            node.BindReference("baseReference", ref BaseReference);
            node.BindReference("derived1Reference", ref Derived1Reference);
            node.BindReference("derived2Reference", ref Derived2Reference);
        }
    }

    [Test]
    public void TestBindSerializableWithReferences()
    {
        // Arrange - Test that objects serialized with BindSerializable can be referenced
        var referencedObject = new ReferenceableObject
        {
            Name = "BindSerializable Object",
            Value = 123
        };

        var container = new BindSerializableContainer
        {
            ContainerName = "BindSerializable Test",
            SerializedObject = referencedObject,
            ObjectReference = referencedObject  // Reference to the same object
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<BindSerializableContainer>(serializedData);

        // Assert - Reference should point to the same object instance
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("BindSerializable Test"));
        Assert.That(deserializedContainer.SerializedObject, Is.Not.Null);
        Assert.That(deserializedContainer.ObjectReference, Is.Not.Null);
        Assert.That(deserializedContainer.SerializedObject!.Name, Is.EqualTo("BindSerializable Object"));
        Assert.That(deserializedContainer.SerializedObject.Value, Is.EqualTo(123));
        
        // Most importantly, the reference should point to the same object instance
        Assert.That(ReferenceEquals(deserializedContainer.SerializedObject, deserializedContainer.ObjectReference), Is.True);
    }

    [Test]
    public void TestBindCollectionSerializableWithReferences()
    {
        // Arrange - Test that objects in collections serialized with BindCollectionSerializable can be referenced
        var object1 = new ReferenceableObject { Name = "Collection Object 1", Value = 10 };
        var object2 = new ReferenceableObject { Name = "Collection Object 2", Value = 20 };
        var object3 = new ReferenceableObject { Name = "Collection Object 3", Value = 30 };

        var container = new CollectionSerializableContainer
        {
            ContainerName = "Collection Serializable Test",
            ObjectCollection = new List<ReferenceableObject> { object1, object2, object3 },
            FirstObjectRef = object1,   // Reference to first object in collection
            SecondObjectRef = object2,  // Reference to second object in collection
            ThirdObjectRef = object3    // Reference to third object in collection
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<CollectionSerializableContainer>(serializedData);

        // Assert - References should point to the same object instances in the collection
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Collection Serializable Test"));
        Assert.That(deserializedContainer.ObjectCollection, Is.Not.Null);
        Assert.That(deserializedContainer.ObjectCollection.Count, Is.EqualTo(3));
        Assert.That(deserializedContainer.FirstObjectRef, Is.Not.Null);
        Assert.That(deserializedContainer.SecondObjectRef, Is.Not.Null);
        Assert.That(deserializedContainer.ThirdObjectRef, Is.Not.Null);

        // Verify collection content
        var collectionArray = deserializedContainer.ObjectCollection.ToArray();
        Assert.That(collectionArray[0].Name, Is.EqualTo("Collection Object 1"));
        Assert.That(collectionArray[1].Name, Is.EqualTo("Collection Object 2"));
        Assert.That(collectionArray[2].Name, Is.EqualTo("Collection Object 3"));

        // Verify references point to the same objects in the collection
        Assert.That(ReferenceEquals(collectionArray[0], deserializedContainer.FirstObjectRef), Is.True);
        Assert.That(ReferenceEquals(collectionArray[1], deserializedContainer.SecondObjectRef), Is.True);
        Assert.That(ReferenceEquals(collectionArray[2], deserializedContainer.ThirdObjectRef), Is.True);
    }

    [Test]
    public void TestMixedSerializationMethodsWithReferences()
    {
        // Arrange - Test mixing different serialization methods with references
        var optionalObject = new ReferenceableObject { Name = "Optional Object", Value = 100 };
        var serializableObject = new ReferenceableObject { Name = "Serializable Object", Value = 200 };
        var collectionObject1 = new ReferenceableObject { Name = "Collection Object 1", Value = 300 };
        var collectionObject2 = new ReferenceableObject { Name = "Collection Object 2", Value = 400 };

        var container = new MixedSerializationContainer
        {
            ContainerName = "Mixed Serialization Test",
            OptionalObject = optionalObject,
            SerializableObject = serializableObject,
            CollectionObjects = new List<ReferenceableObject> { collectionObject1, collectionObject2 },
            OptionalRef = optionalObject,
            SerializableRef = serializableObject,
            Collection1Ref = collectionObject1,
            Collection2Ref = collectionObject2
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser
        var deserializedContainer = BinaryParser.Decode<MixedSerializationContainer>(serializedData);

        // Assert - All references should point to their respective objects
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Mixed Serialization Test"));
        
        // Verify all objects are present
        Assert.That(deserializedContainer.OptionalObject, Is.Not.Null);
        Assert.That(deserializedContainer.SerializableObject, Is.Not.Null);
        Assert.That(deserializedContainer.CollectionObjects, Is.Not.Null);
        Assert.That(deserializedContainer.CollectionObjects.Count, Is.EqualTo(2));
        
        // Verify all references are present
        Assert.That(deserializedContainer.OptionalRef, Is.Not.Null);
        Assert.That(deserializedContainer.SerializableRef, Is.Not.Null);
        Assert.That(deserializedContainer.Collection1Ref, Is.Not.Null);
        Assert.That(deserializedContainer.Collection2Ref, Is.Not.Null);

        // Verify references point to the correct objects
        Assert.That(ReferenceEquals(deserializedContainer.OptionalObject, deserializedContainer.OptionalRef), Is.True);
        Assert.That(ReferenceEquals(deserializedContainer.SerializableObject, deserializedContainer.SerializableRef), Is.True);
        
        var collectionArray = deserializedContainer.CollectionObjects.ToArray();
        Assert.That(ReferenceEquals(collectionArray[0], deserializedContainer.Collection1Ref), Is.True);
        Assert.That(ReferenceEquals(collectionArray[1], deserializedContainer.Collection2Ref), Is.True);

        // Verify object content
        Assert.That(deserializedContainer.OptionalObject!.Name, Is.EqualTo("Optional Object"));
        Assert.That(deserializedContainer.SerializableObject!.Name, Is.EqualTo("Serializable Object"));
        Assert.That(collectionArray[0].Name, Is.EqualTo("Collection Object 1"));
        Assert.That(collectionArray[1].Name, Is.EqualTo("Collection Object 2"));
    }

    /// <summary>
    /// Container that uses BindSerializable to serialize objects.
    /// </summary>
    private class BindSerializableContainer : ISerializable
    {
        public string ContainerName = "";
        public ReferenceableObject? SerializedObject;
        public ReferenceableObject? ObjectReference;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            
            // Use BindSerializable to serialize the object (requires existing object)
            if (SerializedObject != null)
            {
                node.BindSerializable("serializedObject", SerializedObject);
            }
            else if (mode == SerializeMode.Load)
            {
                // For deserialization, we need to create the object first
                SerializedObject = new ReferenceableObject();
                node.BindSerializable("serializedObject", SerializedObject);
            }
            
            // Then bind the reference
            node.BindReference("objectReference", ref ObjectReference);
        }
    }

    /// <summary>
    /// Container that uses BindCollectionSerializable to serialize object collections.
    /// </summary>
    private class CollectionSerializableContainer : ISerializable
    {
        public string ContainerName = "";
        public List<ReferenceableObject> ObjectCollection = new();
        public ReferenceableObject? FirstObjectRef;
        public ReferenceableObject? SecondObjectRef;
        public ReferenceableObject? ThirdObjectRef;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            
            // Use BindCollectionSerializable to serialize the collection
            node.BindCollectionSerializable("objectCollection", ObjectCollection);
            
            // Then bind references to objects in the collection
            node.BindReference("firstObjectRef", ref FirstObjectRef);
            node.BindReference("secondObjectRef", ref SecondObjectRef);
            node.BindReference("thirdObjectRef", ref ThirdObjectRef);
        }
    }

    /// <summary>
    /// Container that uses mixed serialization methods.
    /// </summary>
    private class MixedSerializationContainer : ISerializable
    {
        public string ContainerName = "";
        public ReferenceableObject? OptionalObject;
        public ReferenceableObject? SerializableObject;
        public List<ReferenceableObject> CollectionObjects = new();
        public ReferenceableObject? OptionalRef;
        public ReferenceableObject? SerializableRef;
        public ReferenceableObject? Collection1Ref;
        public ReferenceableObject? Collection2Ref;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            
            // Use BindSerializableOptional
            node.BindSerializableOptional("optionalObject", ref OptionalObject, static (SerializeReadNode subNode) =>
            {
                return new ReferenceableObject();
            });
            
            // Use BindSerializable
            if (SerializableObject != null)
            {
                node.BindSerializable("serializableObject", SerializableObject);
            }
            else if (mode == SerializeMode.Load)
            {
                SerializableObject = new ReferenceableObject();
                node.BindSerializable("serializableObject", SerializableObject);
            }
            
            // Use BindCollectionSerializable
            node.BindCollectionSerializable("collectionObjects", CollectionObjects);
            
            // Bind references to all objects
            node.BindReference("optionalRef", ref OptionalRef);
            node.BindReference("serializableRef", ref SerializableRef);
            node.BindReference("collection1Ref", ref Collection1Ref);
            node.BindReference("collection2Ref", ref Collection2Ref);
        }
    }

    [Test]
    public void TestCollectionObjectsWithReferences()
    {
        // Arrange - Test collection objects that hold references to other objects
        // This tests a scenario where objects within a collection themselves have references
        // to other objects that are also being serialized
        var targetObject1 = new ReferenceableObject
        {
            Name = "Target Object 1",
            Value = 100
        };

        var targetObject2 = new ReferenceableObject
        {
            Name = "Target Object 2", 
            Value = 200
        };

        var collectionObject1 = new ReferenceableObject
        {
            Name = "Collection Object 1",
            Value = 10,
            ChildReference = targetObject1  // Object in collection references external object
        };

        var collectionObject2 = new ReferenceableObject
        {
            Name = "Collection Object 2",
            Value = 20,
            ChildReference = targetObject2  // Object in collection references external object
        };

        var collectionObject3 = new ReferenceableObject
        {
            Name = "Collection Object 3",
            Value = 30,
            ChildReference = collectionObject1  // Object in collection references another object in collection
        };

        var container = new CollectionObjectsWithReferencesContainer
        {
            ContainerName = "Collection Objects With References Test",
            TargetObjects = new List<ReferenceableObject> { targetObject1, targetObject2 },
            CollectionObjects = new List<ReferenceableObject> { collectionObject1, collectionObject2, collectionObject3 },
            DirectTargetRef = targetObject1,  // Direct reference to one of the target objects
            DirectCollectionRef = collectionObject2  // Direct reference to one of the collection objects
        };

        // Act - Serialize using BinaryParser
        byte[] serializedData = BinaryParser.Encode(container);

        // Deserialize using BinaryParser  
        var deserializedContainer = BinaryParser.Decode<CollectionObjectsWithReferencesContainer>(serializedData);

        // Assert - All references should be correctly preserved
        Assert.That(deserializedContainer.ContainerName, Is.EqualTo("Collection Objects With References Test"));
        
        // Verify target objects collection
        Assert.That(deserializedContainer.TargetObjects, Is.Not.Null);
        Assert.That(deserializedContainer.TargetObjects.Count, Is.EqualTo(2));
        var targetArray = deserializedContainer.TargetObjects.ToArray();
        Assert.That(targetArray[0].Name, Is.EqualTo("Target Object 1"));
        Assert.That(targetArray[1].Name, Is.EqualTo("Target Object 2"));

        // Verify collection objects collection
        Assert.That(deserializedContainer.CollectionObjects, Is.Not.Null);
        Assert.That(deserializedContainer.CollectionObjects.Count, Is.EqualTo(3));
        var collectionArray = deserializedContainer.CollectionObjects.ToArray();
        Assert.That(collectionArray[0].Name, Is.EqualTo("Collection Object 1"));
        Assert.That(collectionArray[1].Name, Is.EqualTo("Collection Object 2")); 
        Assert.That(collectionArray[2].Name, Is.EqualTo("Collection Object 3"));

        // Verify direct references
        Assert.That(deserializedContainer.DirectTargetRef, Is.Not.Null);
        Assert.That(deserializedContainer.DirectCollectionRef, Is.Not.Null);
        Assert.That(ReferenceEquals(targetArray[0], deserializedContainer.DirectTargetRef), Is.True);
        Assert.That(ReferenceEquals(collectionArray[1], deserializedContainer.DirectCollectionRef), Is.True);

        // Most importantly: Verify that collection objects' child references are correctly resolved
        // Collection Object 1 should reference Target Object 1
        Assert.That(collectionArray[0].ChildReference, Is.Not.Null);
        Assert.That(ReferenceEquals(targetArray[0], collectionArray[0].ChildReference), Is.True);

        // Collection Object 2 should reference Target Object 2  
        Assert.That(collectionArray[1].ChildReference, Is.Not.Null);
        Assert.That(ReferenceEquals(targetArray[1], collectionArray[1].ChildReference), Is.True);

        // Collection Object 3 should reference Collection Object 1 (intra-collection reference)
        Assert.That(collectionArray[2].ChildReference, Is.Not.Null);
        Assert.That(ReferenceEquals(collectionArray[0], collectionArray[2].ChildReference), Is.True);
    }

    /// <summary>
    /// Container for testing collection objects that hold references to other objects.
    /// </summary>
    private class CollectionObjectsWithReferencesContainer : ISerializable
    {
        public string ContainerName = "";
        public List<ReferenceableObject> TargetObjects = new();
        public List<ReferenceableObject> CollectionObjects = new();
        public ReferenceableObject? DirectTargetRef;
        public ReferenceableObject? DirectCollectionRef;

        public void OnSerialize(SerializeNode node, SerializeMode mode)
        {
            node.BindString("containerName", ref ContainerName);
            
            // First serialize the target objects collection
            node.BindCollectionSerializable("targetObjects", TargetObjects);
            
            // Then serialize the collection objects (which may reference target objects)
            node.BindCollectionSerializable("collectionObjects", CollectionObjects);
            
            // Finally bind direct references
            node.BindReference("directTargetRef", ref DirectTargetRef);
            node.BindReference("directCollectionRef", ref DirectCollectionRef);
        }
    }
}
