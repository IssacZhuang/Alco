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
}