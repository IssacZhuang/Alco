using System;
using System.Linq;
using NUnit.Framework;

namespace Alco.Test;

#pragma warning disable CS0414
#pragma warning disable CS0649

[TestFixture]
public class TestAccessTypeInfo
{
    private class TestClass
    {
        public int PublicProperty { get; set; }
        public int PublicProperty2 { get; private set; }
        public string PublicField;
        private int _privateProperty { get; set; }
        private string _privateField;

        public TestClass()
        {
            PublicProperty = 42;
            PublicField = "Hello";
            _privateProperty = 100;
            _privateField = "Private";
        }
    }

    private struct TestStruct
    {
        public int Number;
        public string Text { get; set; }
    }

    /// <summary>
    /// Test class with an indexer to verify that indexers are not included as regular members.
    /// </summary>
    private class TestClassWithIndexer
    {
        private string[] _items = new string[10];

        public string Name { get; set; } = "Test";

        /// <summary>
        /// Indexer property - this should NOT be included in accessible members.
        /// </summary>
        public string this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        /// <summary>
        /// Another indexer with string key - this should also NOT be included.
        /// </summary>
        public string this[string key]
        {
            get => _items[0];
            set => _items[0] = value;
        }
    }

    /// <summary>
    /// Base class for testing inheritance.
    /// </summary>
    private class BaseTestClass
    {
        public string BaseProperty { get; set; } = "BaseValue";
        public int BaseField = 999;
        protected string ProtectedProperty { get; set; } = "Protected";
    }

    /// <summary>
    /// Derived class for testing inheritance of properties and fields.
    /// </summary>
    private class DerivedTestClass : BaseTestClass
    {
        public string DerivedProperty { get; set; } = "DerivedValue";
        public int DerivedField = 123;
    }

    private MemberAccessor _memberAccessor;

    [SetUp]
    public void Setup()
    {
        _memberAccessor = new ReflectionEmitMemberAccessor();
    }

    [Test]
    public void TestAccessTypeInfoMembers()
    {
        // Arrange
        var typeInfo = new AccessTypeInfo(typeof(TestClass), _memberAccessor);

        // Act
        var members = typeInfo.Members;

        // Assert
        Assert.That(members, Is.Not.Null);
        Assert.That(members.Length, Is.EqualTo(3)); // Should have 3 public members

        // Verify that we have the expected members
        // also is CanRead and CanWrite
        AccessMemberInfo publicProperty = members.First(m => m.Name == "PublicProperty");
        Assert.That(publicProperty.CanRead, Is.True);
        Assert.That(publicProperty.CanWrite, Is.True);

        AccessMemberInfo publicField = members.First(m => m.Name == "PublicField");
        Assert.That(publicField.CanRead, Is.True);
        Assert.That(publicField.CanWrite, Is.True);

        AccessMemberInfo publicProperty2 = members.First(m => m.Name == "PublicProperty2");
        Assert.That(publicProperty2.CanRead, Is.True);
        Assert.That(publicProperty2.CanWrite, Is.False);

        // Verify that private members are not included
        Assert.That(members.Any(m => m.Name == "_privateProperty"), Is.False);
        Assert.That(members.Any(m => m.Name == "_privateField"), Is.False);
    }

    [Test]
    public void TestAccessTypeInfoCreateInstance()
    {
        // Arrange
        var type = typeof(TestClass);
        var constructorInfo = type.GetConstructor(Type.EmptyTypes);
        var typeInfo = new AccessTypeInfo(type, _memberAccessor);

        // Act
        var instance = typeInfo.CreateInstance<TestClass>();

        // Assert
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.PublicProperty, Is.EqualTo(42));
        Assert.That(instance.PublicField, Is.EqualTo("Hello"));
    }

    [Test]
    public void TestAccessTypeInfoWithStruct()
    {
        // Arrange
        var typeInfo = new AccessTypeInfo(typeof(TestStruct), _memberAccessor);

        // Act
        var members = typeInfo.Members;

        // Assert
        Assert.That(members, Is.Not.Null);
        Assert.That(members.Length, Is.EqualTo(2)); // Should have 2 public members

        // Verify that we have the expected members
        Assert.That(members.Any(m => m.Name == "Number"), Is.True);
        Assert.That(members.Any(m => m.Name == "Text"), Is.True);
    }

    [Test]
    public void TestAccessTypeInfoWithNoParameterlessConstructor()
    {
        // Arrange
        var typeInfo = new AccessTypeInfo(typeof(string), _memberAccessor);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => typeInfo.CreateInstance<string>());
    }

    /// <summary>
    /// Test that indexers are not included as accessible members.
    /// Indexers should be filtered out because they have parameters and are not regular properties.
    /// </summary>
    [Test]
    public void TestAccessTypeInfoExcludesIndexers()
    {
        // Arrange
        var typeInfo = new AccessTypeInfo(typeof(TestClassWithIndexer), _memberAccessor);

        // Act
        var members = typeInfo.Members;

        // Assert
        Assert.That(members, Is.Not.Null);

        // Should only have the Name property, not the indexers
        Assert.That(members.Length, Is.EqualTo(1), "Should only contain the Name property, not indexers");
        Assert.That(members[0].Name, Is.EqualTo("Name"), "The only member should be the Name property");

        // Verify that indexers are not included
        // Indexers have the name "Item" in C#
        Assert.That(members.Any(m => m.Name == "Item"), Is.False, "Indexers should not be included as accessible members");

        // Verify we can access the Name property
        var instance = typeInfo.CreateInstance<TestClassWithIndexer>();
        Assert.That(members[0].GetValue<string>(instance), Is.EqualTo("Test"));

        members[0].SetValue(instance, "Modified");
        Assert.That(instance.Name, Is.EqualTo("Modified"));
    }

    /// <summary>
    /// Test that inherited properties and fields from base classes are included.
    /// This test reveals the bug where DeclaredOnly flag prevents inheritance.
    /// </summary>
    [Test]
    public void TestAccessTypeInfoIncludesInheritedMembers()
    {
        // Arrange
        var typeInfo = new AccessTypeInfo(typeof(DerivedTestClass), _memberAccessor);

        // Act
        var members = typeInfo.Members;

        // Assert
        Assert.That(members, Is.Not.Null);

        // Should include both derived and inherited members
        // Expected: DerivedProperty, DerivedField, BaseProperty, BaseField (4 total)
        // Current behavior with DeclaredOnly: only DerivedProperty, DerivedField (2 total)

        var memberNames = members.Select(m => m.Name).ToArray();
        Console.WriteLine($"Found members: {string.Join(", ", memberNames)}");

        // Test for derived class members (these should always be present)
        Assert.That(members.Any(m => m.Name == "DerivedProperty"), Is.True, "Should include DerivedProperty");
        Assert.That(members.Any(m => m.Name == "DerivedField"), Is.True, "Should include DerivedField");

        // Test for inherited members (these are currently missing due to DeclaredOnly)
        Assert.That(members.Any(m => m.Name == "BaseProperty"), Is.True, "Should include inherited BaseProperty");
        Assert.That(members.Any(m => m.Name == "BaseField"), Is.True, "Should include inherited BaseField");

        // Protected members should not be included
        Assert.That(members.Any(m => m.Name == "ProtectedProperty"), Is.False, "Should not include protected members");

        // Verify total count
        Assert.That(members.Length, Is.EqualTo(4), "Should have 4 public members (2 derived + 2 inherited)");

        // Test functionality with inherited members
        var instance = typeInfo.CreateInstance<DerivedTestClass>();

        var basePropertyMember = members.First(m => m.Name == "BaseProperty");
        Assert.That(basePropertyMember.GetValue<string>(instance), Is.EqualTo("BaseValue"));

        basePropertyMember.SetValue(instance, "ModifiedBase");
        Assert.That(instance.BaseProperty, Is.EqualTo("ModifiedBase"));
    }
}