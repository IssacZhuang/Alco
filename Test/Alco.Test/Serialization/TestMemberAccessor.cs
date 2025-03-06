using System;
using NUnit.Framework;

namespace Alco.Test;

[TestFixture]
public class TestMemberAccessor
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

        public TestClass(int publicProperty, string publicField)
        {
            PublicProperty = publicProperty;
            PublicField = publicField;
            _privateProperty = 0;
            _privateField = string.Empty;
        }
    }

    private struct TestStruct
    {
        public int Number;
        public string Text { get; set; }
    }

    private ReflectionMemberAccessor _reflectionAccessor;
    private ReflectionEmitMemberAccessor _reflectionEmitAccessor;

    [SetUp]
    public void Setup()
    {
        _reflectionAccessor = new ReflectionMemberAccessor();
        _reflectionEmitAccessor = new ReflectionEmitMemberAccessor();
    }

    [Test]
    public void TestParameterlessConstructor()
    {
        // Test class constructor
        var classType = typeof(TestClass);
        var classCtor = classType.GetConstructor(Type.EmptyTypes);

        var reflectionClassCtor = _reflectionAccessor.CreateParameterlessConstructor(classType, classCtor);
        var reflectionEmitClassCtor = _reflectionEmitAccessor.CreateParameterlessConstructor(classType, classCtor);

        Assert.That(reflectionClassCtor, Is.Not.Null);
        Assert.That(reflectionEmitClassCtor, Is.Not.Null);

        var reflectionInstance = (TestClass)reflectionClassCtor!();
        var reflectionEmitInstance = (TestClass)reflectionEmitClassCtor!();

        Assert.That(reflectionInstance.PublicProperty, Is.EqualTo(42));
        Assert.That(reflectionEmitInstance.PublicProperty, Is.EqualTo(42));

        // Test struct constructor
        var structType = typeof(TestStruct);
        var structCtor = structType.GetConstructor(Type.EmptyTypes);

        var reflectionStructCtor = _reflectionAccessor.CreateParameterlessConstructor(structType, structCtor);
        var reflectionEmitStructCtor = _reflectionEmitAccessor.CreateParameterlessConstructor(structType, structCtor);

        Assert.That(reflectionStructCtor, Is.Not.Null);
        Assert.That(reflectionEmitStructCtor, Is.Not.Null);

        var reflectionStructInstance = (TestStruct)reflectionStructCtor!();
        var reflectionEmitStructInstance = (TestStruct)reflectionEmitStructCtor!();

        Assert.That(reflectionStructInstance.Number, Is.EqualTo(0));
        Assert.That(reflectionEmitStructInstance.Number, Is.EqualTo(0));
    }

    [Test]
    public void TestParameterizedConstructor()
    {
        var classType = typeof(TestClass);
        var classCtor = classType.GetConstructor(new[] { typeof(int), typeof(string) });

        var reflectionCtor = _reflectionAccessor.CreateParameterizedConstructor<TestClass>(classCtor!);
        var reflectionEmitCtor = _reflectionEmitAccessor.CreateParameterizedConstructor<TestClass>(classCtor!);

        var reflectionInstance = reflectionCtor(new object[] { 100, "Test" });
        var reflectionEmitInstance = reflectionEmitCtor(new object[] { 100, "Test" });

        Assert.That(reflectionInstance.PublicProperty, Is.EqualTo(100));
        Assert.That(reflectionInstance.PublicField, Is.EqualTo("Test"));
        Assert.That(reflectionEmitInstance.PublicProperty, Is.EqualTo(100));
        Assert.That(reflectionEmitInstance.PublicField, Is.EqualTo("Test"));
    }

    [Test]
    public void TestParameterizedConstructorWithDelegate()
    {
        var classType = typeof(TestClass);
        var classCtor = classType.GetConstructor(new[] { typeof(int), typeof(string) });

        var reflectionCtor = _reflectionAccessor.CreateParameterizedConstructor<TestClass, int, string, object, object>(classCtor!);
        var reflectionEmitCtor = _reflectionEmitAccessor.CreateParameterizedConstructor<TestClass, int, string, object, object>(classCtor!);

        var reflectionInstance = reflectionCtor(100, "Test", null, null);
        var reflectionEmitInstance = reflectionEmitCtor(100, "Test", null, null);

        Assert.That(reflectionInstance.PublicProperty, Is.EqualTo(100));
        Assert.That(reflectionInstance.PublicField, Is.EqualTo("Test"));
        Assert.That(reflectionEmitInstance.PublicProperty, Is.EqualTo(100));
        Assert.That(reflectionEmitInstance.PublicField, Is.EqualTo("Test"));
    }

    [Test]
    public void TestPropertyGetterAndSetter()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.PublicProperty))!;

        var reflectionGetter = _reflectionAccessor.CreatePropertyGetter<int>(propertyInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreatePropertyGetter<int>(propertyInfo);
        var reflectionSetter = _reflectionAccessor.CreatePropertySetter<int>(propertyInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreatePropertySetter<int>(propertyInfo);

        var instance = new TestClass();

        // Test getters
        Assert.That(reflectionGetter(instance), Is.EqualTo(42));
        Assert.That(reflectionEmitGetter(instance), Is.EqualTo(42));

        // Test setters
        reflectionSetter(instance, 200);
        Assert.That(instance.PublicProperty, Is.EqualTo(200));

        reflectionEmitSetter(instance, 300);
        Assert.That(instance.PublicProperty, Is.EqualTo(300));
    }

    [Test]
    public void TestFieldGetterAndSetter()
    {
        var classType = typeof(TestClass);
        var fieldInfo = classType.GetField(nameof(TestClass.PublicField))!;

        var reflectionGetter = _reflectionAccessor.CreateFieldGetter<string>(fieldInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreateFieldGetter<string>(fieldInfo);
        var reflectionSetter = _reflectionAccessor.CreateFieldSetter<string>(fieldInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreateFieldSetter<string>(fieldInfo);

        var instance = new TestClass();

        // Test getters
        Assert.That(reflectionGetter(instance), Is.EqualTo("Hello"));
        Assert.That(reflectionEmitGetter(instance), Is.EqualTo("Hello"));

        // Test setters
        reflectionSetter(instance, "World");
        Assert.That(instance.PublicField, Is.EqualTo("World"));

        reflectionEmitSetter(instance, "Test");
        Assert.That(instance.PublicField, Is.EqualTo("Test"));
    }

    [Test]
    public void TestStructFieldAndProperty()
    {
        var structType = typeof(TestStruct);
        var fieldInfo = structType.GetField(nameof(TestStruct.Number))!;
        var propertyInfo = structType.GetProperty(nameof(TestStruct.Text))!;

        var reflectionFieldGetter = _reflectionAccessor.CreateFieldGetter<int>(fieldInfo);
        var reflectionEmitFieldGetter = _reflectionEmitAccessor.CreateFieldGetter<int>(fieldInfo);
        var reflectionPropertyGetter = _reflectionAccessor.CreatePropertyGetter<string>(propertyInfo);
        var reflectionEmitPropertyGetter = _reflectionEmitAccessor.CreatePropertyGetter<string>(propertyInfo);

        var instance = new TestStruct { Number = 42, Text = "Test" };

        // Test field getters
        Assert.That(reflectionFieldGetter(instance), Is.EqualTo(42));
        Assert.That(reflectionEmitFieldGetter(instance), Is.EqualTo(42));

        // Test property getters
        Assert.That(reflectionPropertyGetter(instance), Is.EqualTo("Test"));
        Assert.That(reflectionEmitPropertyGetter(instance), Is.EqualTo("Test"));
    }
}