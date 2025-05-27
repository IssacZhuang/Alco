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

        // Array fields and properties for testing
        public int[] ValueTypeArray { get; set; }
        public string[] ObjectTypeArray;
        public object[] MixedObjectArray { get; set; }

        private int _privateProperty { get; set; }
        private string _privateField;

        public TestClass()
        {
            PublicProperty = 42;
            PublicField = "Hello";
            _privateProperty = 100;
            _privateField = "Private";

            // Initialize arrays
            ValueTypeArray = new int[] { 1, 2, 3, 4, 5 };
            ObjectTypeArray = new string[] { "First", "Second", "Third" };
            MixedObjectArray = new object[] { 1, "String", 3.14, true };
        }

        public TestClass(int publicProperty, string publicField)
        {
            PublicProperty = publicProperty;
            PublicField = publicField;
            _privateProperty = 0;
            _privateField = string.Empty;

            // Initialize arrays with default values
            ValueTypeArray = new int[] { 0 };
            ObjectTypeArray = new string[] { string.Empty };
            MixedObjectArray = new object[] { null };
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

    [Test]
    public void TestValueTypeArrayProperty()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.ValueTypeArray))!;

        var reflectionGetter = _reflectionAccessor.CreatePropertyGetter<int[]>(propertyInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreatePropertyGetter<int[]>(propertyInfo);
        var reflectionSetter = _reflectionAccessor.CreatePropertySetter<int[]>(propertyInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreatePropertySetter<int[]>(propertyInfo);

        var instance = new TestClass();

        // Test getters
        var reflectionArray = reflectionGetter(instance);
        var reflectionEmitArray = reflectionEmitGetter(instance);

        Assert.That(reflectionArray, Is.Not.Null);
        Assert.That(reflectionEmitArray, Is.Not.Null);
        Assert.That(reflectionArray, Is.EqualTo(new int[] { 1, 2, 3, 4, 5 }));
        Assert.That(reflectionEmitArray, Is.EqualTo(new int[] { 1, 2, 3, 4, 5 }));

        // Test setters
        var newArray = new int[] { 10, 20, 30 };
        reflectionSetter(instance, newArray);
        Assert.That(instance.ValueTypeArray, Is.EqualTo(newArray));

        var anotherArray = new int[] { 100, 200 };
        reflectionEmitSetter(instance, anotherArray);
        Assert.That(instance.ValueTypeArray, Is.EqualTo(anotherArray));
    }

    [Test]
    public void TestObjectTypeArrayField()
    {
        var classType = typeof(TestClass);
        var fieldInfo = classType.GetField(nameof(TestClass.ObjectTypeArray))!;

        var reflectionGetter = _reflectionAccessor.CreateFieldGetter<string[]>(fieldInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreateFieldGetter<string[]>(fieldInfo);
        var reflectionSetter = _reflectionAccessor.CreateFieldSetter<string[]>(fieldInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreateFieldSetter<string[]>(fieldInfo);

        var instance = new TestClass();

        // Test getters
        var reflectionArray = reflectionGetter(instance);
        var reflectionEmitArray = reflectionEmitGetter(instance);

        Assert.That(reflectionArray, Is.Not.Null);
        Assert.That(reflectionEmitArray, Is.Not.Null);
        Assert.That(reflectionArray, Is.EqualTo(new string[] { "First", "Second", "Third" }));
        Assert.That(reflectionEmitArray, Is.EqualTo(new string[] { "First", "Second", "Third" }));

        // Test setters
        var newArray = new string[] { "Apple", "Banana", "Cherry" };
        reflectionSetter(instance, newArray);
        Assert.That(instance.ObjectTypeArray, Is.EqualTo(newArray));

        var anotherArray = new string[] { "Hello", "World" };
        reflectionEmitSetter(instance, anotherArray);
        Assert.That(instance.ObjectTypeArray, Is.EqualTo(anotherArray));
    }

    [Test]
    public void TestMixedObjectArrayProperty()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.MixedObjectArray))!;

        var reflectionGetter = _reflectionAccessor.CreatePropertyGetter<object[]>(propertyInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreatePropertyGetter<object[]>(propertyInfo);
        var reflectionSetter = _reflectionAccessor.CreatePropertySetter<object[]>(propertyInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreatePropertySetter<object[]>(propertyInfo);

        var instance = new TestClass();

        // Test getters
        var reflectionArray = reflectionGetter(instance);
        var reflectionEmitArray = reflectionEmitGetter(instance);

        Assert.That(reflectionArray, Is.Not.Null);
        Assert.That(reflectionEmitArray, Is.Not.Null);
        Assert.That(reflectionArray.Length, Is.EqualTo(4));
        Assert.That(reflectionEmitArray.Length, Is.EqualTo(4));
        Assert.That(reflectionArray[0], Is.EqualTo(1));
        Assert.That(reflectionArray[1], Is.EqualTo("String"));
        Assert.That(reflectionArray[2], Is.EqualTo(3.14));
        Assert.That(reflectionArray[3], Is.EqualTo(true));

        // Test setters
        var newArray = new object[] { "New", 42, false };
        reflectionSetter(instance, newArray);
        Assert.That(instance.MixedObjectArray, Is.EqualTo(newArray));

        var anotherArray = new object[] { 99, "Test", null, 2.5 };
        reflectionEmitSetter(instance, anotherArray);
        Assert.That(instance.MixedObjectArray, Is.EqualTo(anotherArray));
    }

    [Test]
    public void TestNullArrayHandling()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.ValueTypeArray))!;

        var reflectionSetter = _reflectionAccessor.CreatePropertySetter<int[]>(propertyInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreatePropertySetter<int[]>(propertyInfo);
        var reflectionGetter = _reflectionAccessor.CreatePropertyGetter<int[]>(propertyInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreatePropertyGetter<int[]>(propertyInfo);

        var instance = new TestClass();

        // Test setting null arrays
        reflectionSetter(instance, null);
        Assert.That(instance.ValueTypeArray, Is.Null);
        Assert.That(reflectionGetter(instance), Is.Null);

        reflectionEmitSetter(instance, null);
        Assert.That(instance.ValueTypeArray, Is.Null);
        Assert.That(reflectionEmitGetter(instance), Is.Null);
    }

    [Test]
    public void TestEmptyArrayHandling()
    {
        var classType = typeof(TestClass);
        var fieldInfo = classType.GetField(nameof(TestClass.ObjectTypeArray))!;

        var reflectionSetter = _reflectionAccessor.CreateFieldSetter<string[]>(fieldInfo);
        var reflectionEmitSetter = _reflectionEmitAccessor.CreateFieldSetter<string[]>(fieldInfo);
        var reflectionGetter = _reflectionAccessor.CreateFieldGetter<string[]>(fieldInfo);
        var reflectionEmitGetter = _reflectionEmitAccessor.CreateFieldGetter<string[]>(fieldInfo);

        var instance = new TestClass();

        // Test setting empty arrays
        var emptyArray = new string[0];
        reflectionSetter(instance, emptyArray);
        Assert.That(instance.ObjectTypeArray, Is.EqualTo(emptyArray));
        Assert.That(reflectionGetter(instance), Is.EqualTo(emptyArray));

        reflectionEmitSetter(instance, emptyArray);
        Assert.That(instance.ObjectTypeArray, Is.EqualTo(emptyArray));
        Assert.That(reflectionEmitGetter(instance), Is.EqualTo(emptyArray));
    }
}