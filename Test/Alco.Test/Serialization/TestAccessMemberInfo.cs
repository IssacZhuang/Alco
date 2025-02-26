using System;
using System.Reflection;
using NUnit.Framework;

namespace Alco.Test;

[TestFixture]
public class TestAccessMemberInfo
{
    private class TestClass
    {
        public int IntProperty { get; set; }
        public string StringProperty { get; set; }
        public float FloatField;
        public bool BoolProperty { get; private set; }

        public TestClass()
        {
            IntProperty = 42;
            StringProperty = "Hello";
            FloatField = 3.14f;
            BoolProperty = true;
        }

        public void SetBoolProperty(bool value)
        {
            BoolProperty = value;
        }
    }

    private MemberAccessor _memberAccessor;

    [SetUp]
    public void Setup()
    {
        _memberAccessor = new ReflectionEmitMemberAccessor();
    }

    [Test]
    public void TestCreatePropertyInfo()
    {
        // Arrange
        var propertyInfo = typeof(TestClass).GetProperty("IntProperty");

        // Act
        var accessMemberInfo = AccessMemberInfo.Create(propertyInfo, _memberAccessor);

        // Assert
        Assert.That(accessMemberInfo, Is.Not.Null);
        Assert.That(accessMemberInfo.Name, Is.EqualTo("IntProperty"));
        Assert.That(accessMemberInfo.MemberType, Is.EqualTo(typeof(int)));
        Assert.That(accessMemberInfo.CanRead, Is.True);
        Assert.That(accessMemberInfo.CanWrite, Is.True);
    }

    [Test]
    public void TestCreateFieldInfo()
    {
        // Arrange
        var fieldInfo = typeof(TestClass).GetField("FloatField");

        // Act
        var accessMemberInfo = AccessMemberInfo.Create(fieldInfo, _memberAccessor);

        // Assert
        Assert.That(accessMemberInfo, Is.Not.Null);
        Assert.That(accessMemberInfo.Name, Is.EqualTo("FloatField"));
        Assert.That(accessMemberInfo.MemberType, Is.EqualTo(typeof(float)));
        Assert.That(accessMemberInfo.CanRead, Is.True);
        Assert.That(accessMemberInfo.CanWrite, Is.True);
    }

    [Test]
    public void TestGetValueGeneric()
    {
        // Arrange
        var instance = new TestClass();
        var propertyInfo = typeof(TestClass).GetProperty("IntProperty");
        var accessMemberInfo = AccessMemberInfo.Create(propertyInfo, _memberAccessor);

        // Act
        var value = accessMemberInfo.GetValue<int>(instance);

        // Assert
        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void TestGetValueString()
    {
        // Arrange
        var instance = new TestClass();
        var propertyInfo = typeof(TestClass).GetProperty("StringProperty");
        var accessMemberInfo = AccessMemberInfo.Create(propertyInfo, _memberAccessor);

        // Act
        var value = accessMemberInfo.GetValue<string>(instance);

        // Assert
        Assert.That(value, Is.EqualTo("Hello"));
    }

    [Test]
    public void TestGetValueField()
    {
        // Arrange
        var instance = new TestClass();
        var fieldInfo = typeof(TestClass).GetField("FloatField");
        var accessMemberInfo = AccessMemberInfo.Create(fieldInfo, _memberAccessor);

        // Act
        var value = accessMemberInfo.GetValue<float>(instance);

        // Assert
        Assert.That(value, Is.EqualTo(3.14f));
    }

    [Test]
    public void TestSetValueProperty()
    {
        // Arrange
        var instance = new TestClass();
        var propertyInfo = typeof(TestClass).GetProperty("IntProperty");
        var accessMemberInfo = AccessMemberInfo.Create(propertyInfo, _memberAccessor);

        // Act
        accessMemberInfo.SetValue<int>(instance, 100);

        // Assert
        Assert.That(instance.IntProperty, Is.EqualTo(100));
    }

    [Test]
    public void TestSetValueField()
    {
        // Arrange
        var instance = new TestClass();
        var fieldInfo = typeof(TestClass).GetField("FloatField");
        var accessMemberInfo = AccessMemberInfo.Create(fieldInfo, _memberAccessor);

        // Act
        accessMemberInfo.SetValue<float>(instance, 6.28f);

        // Assert
        Assert.That(instance.FloatField, Is.EqualTo(6.28f));
    }

    [Test]
    public void TestReadOnlyProperty()
    {
        // Arrange
        var instance = new TestClass();
        var propertyInfo = typeof(TestClass).GetProperty("BoolProperty");
        var accessMemberInfo = AccessMemberInfo.Create(propertyInfo, _memberAccessor);

        // Act & Assert
        Assert.That(accessMemberInfo.CanRead, Is.True);
        Assert.That(accessMemberInfo.CanWrite, Is.False);

        // Should be able to read
        var value = accessMemberInfo.GetValue<bool>(instance);
        Assert.That(value, Is.EqualTo(true));

        // Change the value through a method
        instance.SetBoolProperty(false);

        // Should reflect the new value
        value = accessMemberInfo.GetValue<bool>(instance);
        Assert.That(value, Is.EqualTo(false));
    }

    [Test]
    public void TestGetValueWrongType()
    {
        // Arrange
        var instance = new TestClass();
        var propertyInfo = typeof(TestClass).GetProperty("IntProperty");
        var accessMemberInfo = AccessMemberInfo.Create(propertyInfo, _memberAccessor);

        // Act & Assert
        // InvalidCastException
        Assert.Throws<InvalidCastException>(() => accessMemberInfo.GetValue<string>(instance));
    }
}
