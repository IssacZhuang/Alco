using System;
using NUnit.Framework;

namespace Alco.Test;

[TestFixture]
public class TestDynamicAccessor
{
    private class TestClass
    {
        public int PublicProperty { get; set; }
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

    [Test]
    public void TestPropertyAccess()
    {
        var obj = new TestClass();

        // Test getting public property
        Assert.That(DynamicAccessor<TestClass>.TryGetValue(obj, "PublicProperty", out object value), Is.True);
        Assert.That(value, Is.EqualTo(42));

        // Test setting public property
        Assert.That(DynamicAccessor<TestClass>.TrySetValue(obj, "PublicProperty", 100), Is.True);
        Assert.That(obj.PublicProperty, Is.EqualTo(100));

        // Test non-existent property
        Assert.That(DynamicAccessor<TestClass>.TryGetValue(obj, "NonExistentProperty", out _), Is.False);
        Assert.That(DynamicAccessor<TestClass>.TrySetValue(obj, "NonExistentProperty", 200), Is.False);
    }

    [Test]
    public void TestFieldAccess()
    {
        var obj = new TestClass();

        // Test getting public field
        Assert.That(DynamicAccessor<TestClass>.TryGetValue(obj, "PublicField", out object value), Is.True);
        Assert.That(value, Is.EqualTo("Hello"));

        // Test setting public field
        Assert.That(DynamicAccessor<TestClass>.TrySetValue(obj, "PublicField", "World"), Is.True);
        Assert.That(obj.PublicField, Is.EqualTo("World"));
    }

    [Test]
    public void TestStructAccess()
    {
        var obj = new TestStruct
        {
            Number = 42,
            Text = "Test"
        };

        // Test getting struct field
        Assert.That(DynamicAccessor<TestStruct>.TryGetValue(obj, "Number", out object value), Is.True);
        Assert.That(value, Is.EqualTo(42));

        // Test getting struct property
        Assert.That(DynamicAccessor<TestStruct>.TryGetValue(obj, "Text", out object textValue), Is.True);
        Assert.That(textValue, Is.EqualTo("Test"));
    }

    [Test]
    public void TestTypeSafety()
    {
        var obj = new TestClass();

        // Test setting property with wrong type
        Assert.That(DynamicAccessor<TestClass>.TrySetValue(obj, "PublicProperty", "wrong type"), Is.False);

        // Test setting field with wrong type
        Assert.That(DynamicAccessor<TestClass>.TrySetValue(obj, "PublicField", 42), Is.False);
    }

    [Test]
    public void TestGetSetValue()
    {
        var obj = new TestClass();

        // Test GetValue
        Assert.That(() => DynamicAccessor<TestClass>.GetValue(obj, "PublicProperty"), Is.EqualTo(42));
        Assert.That(() => DynamicAccessor<TestClass>.GetValue(obj, "NonExistent"),
            Throws.ArgumentException.With.Message.Contains("not found"));

        // Test SetValue
        Assert.That(() => DynamicAccessor<TestClass>.SetValue(obj, "PublicProperty", 200), Throws.Nothing);
        Assert.That(() => DynamicAccessor<TestClass>.SetValue(obj, "NonExistent", 100),
            Throws.ArgumentException.With.Message.Contains("not found"));
        Assert.That(() => DynamicAccessor<TestClass>.SetValue(obj, "PublicProperty", "wrong type"),
            Throws.ArgumentException.With.Message.Contains("not assignable"));
    }

    [Test]
    public void TestPropertyAndFieldNames()
    {
        // Test property names
        var propertyNames = DynamicAccessor<TestClass>.PropertyNames;
        Assert.That(propertyNames, Contains.Item("PublicProperty"));
        Assert.That(propertyNames, Contains.Item("_privateProperty"));

        // Test field names
        var fieldNames = DynamicAccessor<TestClass>.FieldNames;
        Assert.That(fieldNames, Contains.Item("PublicField"));
        Assert.That(fieldNames, Contains.Item("_privateField"));
    }
}