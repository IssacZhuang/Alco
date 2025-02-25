using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Alco.Test;

[TestFixture]
public class TestMemberAccessorCache
{
    private class TestClass
    {
        public int PublicProperty { get; set; }
        public string PublicField;

        public TestClass()
        {
            PublicProperty = 42;
            PublicField = "Hello";
        }

        public TestClass(int publicProperty, string publicField)
        {
            PublicProperty = publicProperty;
            PublicField = publicField;
        }
    }

    private ReflectionMemberAccessor _underlyingAccessor;
    private MemberAccessorCache _cacheAccessor;
    private MockMemberAccessor _mockAccessor;

    [SetUp]
    public void Setup()
    {
        _underlyingAccessor = new ReflectionMemberAccessor();
        _mockAccessor = new MockMemberAccessor(_underlyingAccessor);
        _cacheAccessor = new MemberAccessorCache(_mockAccessor);
    }

    /// <summary>
    /// Mock accessor that counts method calls to verify cache hits/misses
    /// </summary>
    private class MockMemberAccessor : MemberAccessor
    {
        private readonly MemberAccessor _innerAccessor;
        public int CallCount { get; private set; }

        public MockMemberAccessor(MemberAccessor innerAccessor)
        {
            _innerAccessor = innerAccessor;
        }

        public void ResetCallCount()
        {
            CallCount = 0;
        }

        public override Action<TCollection, object> CreateAddMethodDelegate<TCollection>()
        {
            CallCount++;
            return _innerAccessor.CreateAddMethodDelegate<TCollection>();
        }

        public override Func<object, TProperty> CreateFieldGetter<TProperty>(FieldInfo fieldInfo)
        {
            CallCount++;
            return _innerAccessor.CreateFieldGetter<TProperty>(fieldInfo);
        }

        public override Action<object, TProperty> CreateFieldSetter<TProperty>(FieldInfo fieldInfo)
        {
            CallCount++;
            return _innerAccessor.CreateFieldSetter<TProperty>(fieldInfo);
        }

        public override Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>()
        {
            CallCount++;
            return _innerAccessor.CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>();
        }

        public override Func<IEnumerable<TElement>, TCollection> CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>()
        {
            CallCount++;
            return _innerAccessor.CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>();
        }

        public override Func<object[], T> CreateParameterizedConstructor<T>(ConstructorInfo constructor)
        {
            CallCount++;
            return _innerAccessor.CreateParameterizedConstructor<T>(constructor);
        }

        public override ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3> CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>(ConstructorInfo constructor)
        {
            CallCount++;
            return _innerAccessor.CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>(constructor);
        }

        public override Func<object> CreateParameterlessConstructor(Type type, ConstructorInfo constructorInfo)
        {
            CallCount++;
            return _innerAccessor.CreateParameterlessConstructor(type, constructorInfo);
        }

        public override Func<object, TProperty> CreatePropertyGetter<TProperty>(PropertyInfo propertyInfo)
        {
            CallCount++;
            return _innerAccessor.CreatePropertyGetter<TProperty>(propertyInfo);
        }

        public override Action<object, TProperty> CreatePropertySetter<TProperty>(PropertyInfo propertyInfo)
        {
            CallCount++;
            return _innerAccessor.CreatePropertySetter<TProperty>(propertyInfo);
        }

        public override void Clear()
        {
            // No need to implement for testing
        }
    }

    [Test]
    public void TestPropertyGetterCache()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.PublicProperty))!;

        // First call should go through to the underlying accessor
        var getter1 = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        // Second call with the same property should use the cache
        var getter2 = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1), "Second call should not increase call count");

        // Both getters should work correctly
        var instance = new TestClass();
        Assert.That(getter1(instance), Is.EqualTo(42));
        Assert.That(getter2(instance), Is.EqualTo(42));
    }

    [Test]
    public void TestFieldGetterCache()
    {
        var classType = typeof(TestClass);
        var fieldInfo = classType.GetField(nameof(TestClass.PublicField))!;

        // First call should go through to the underlying accessor
        var getter1 = _cacheAccessor.CreateFieldGetter<string>(fieldInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        // Second call with the same field should use the cache
        var getter2 = _cacheAccessor.CreateFieldGetter<string>(fieldInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1), "Second call should not increase call count");

        // Both getters should work correctly
        var instance = new TestClass();
        Assert.That(getter1(instance), Is.EqualTo("Hello"));
        Assert.That(getter2(instance), Is.EqualTo("Hello"));

        //reference equal
        Assert.That(getter1, Is.SameAs(getter2));
    }

    [Test]
    public void TestParameterlessConstructorCache()
    {
        var classType = typeof(TestClass);
        var classCtor = classType.GetConstructor(Type.EmptyTypes);

        // First call should go through to the underlying accessor
        _mockAccessor.ResetCallCount();
        var ctor1 = _cacheAccessor.CreateParameterlessConstructor(classType, classCtor);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        // Second call with the same constructor should use the cache
        var ctor2 = _cacheAccessor.CreateParameterlessConstructor(classType, classCtor);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1), "Second call should not increase call count");

        // Both constructors should work correctly
        var instance1 = (TestClass)ctor1!();
        var instance2 = (TestClass)ctor2!();
        Assert.That(instance1.PublicProperty, Is.EqualTo(42));
        Assert.That(instance2.PublicProperty, Is.EqualTo(42));

        //reference equal
        Assert.That(ctor1, Is.SameAs(ctor2));
    }

    [Test]
    public void TestParameterizedConstructorCache()
    {
        var classType = typeof(TestClass);
        var classCtor = classType.GetConstructor(new[] { typeof(int), typeof(string) });

        // First call should go through to the underlying accessor
        _mockAccessor.ResetCallCount();
        var ctor1 = _cacheAccessor.CreateParameterizedConstructor<TestClass>(classCtor!);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        // Second call with the same constructor should use the cache
        var ctor2 = _cacheAccessor.CreateParameterizedConstructor<TestClass>(classCtor!);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1), "Second call should not increase call count");

        // Both constructors should work correctly
        var instance1 = ctor1(new object[] { 100, "Test" });
        var instance2 = ctor2(new object[] { 100, "Test" });
        Assert.That(instance1.PublicProperty, Is.EqualTo(100));
        Assert.That(instance2.PublicProperty, Is.EqualTo(100));

        //reference equal
        Assert.That(ctor1, Is.SameAs(ctor2));
    }

    [Test]
    public void TestCacheClear()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.PublicProperty))!;

        // First call should go through to the underlying accessor
        _mockAccessor.ResetCallCount();
        var getter1 = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        // Second call should use the cache
        var getter2 = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        //reference equal
        Assert.That(getter1, Is.SameAs(getter2));

        // Clear the cache
        _cacheAccessor.Clear();

        // After clearing, should go through to the underlying accessor again
        var getter3 = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(2), "Call count should increase after cache clear");

        //reference not equal
        Assert.That(getter1, Is.Not.SameAs(getter3));
    }

    [Test]
    public void TestDifferentGenericTypesAreCached()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.PublicProperty))!;

        // First call with int
        _mockAccessor.ResetCallCount();
        _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(1));

        // Call with object should be a different cache entry
        _cacheAccessor.CreatePropertyGetter<object>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(2), "Different generic types should be separate cache entries");

        // Repeat calls should use cache
        _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        _cacheAccessor.CreatePropertyGetter<object>(propertyInfo);
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(2), "Repeat calls should use cache");
    }

    [Test]
    public void TestConcurrentAccess()
    {
        var classType = typeof(TestClass);
        var propertyInfo = classType.GetProperty(nameof(TestClass.PublicProperty))!;
        var fieldInfo = classType.GetField(nameof(TestClass.PublicField))!;
        var ctor = classType.GetConstructor(Type.EmptyTypes);

        // Warm up the cache first to ensure the delegates are already created
        var initialPropGetter = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
        var initialFldGetter = _cacheAccessor.CreateFieldGetter<string>(fieldInfo);
        var initialCtorDel = _cacheAccessor.CreateParameterlessConstructor(classType, ctor);

        // Now reset the call count after warming up the cache
        _mockAccessor.ResetCallCount();

        // Record initial call count (should be 0 after reset)
        int initialCallCount = _mockAccessor.CallCount;

        var propertyGetterResults = new Func<object, int>[10];
        var fieldGetterResults = new Func<object, string>[10];
        var ctorResults = new Func<object>[10];

        Parallel.For(0, 10, index =>
        {
            // Access different methods and store the results
            var propGetter = _cacheAccessor.CreatePropertyGetter<int>(propertyInfo);
            var fldGetter = _cacheAccessor.CreateFieldGetter<string>(fieldInfo);
            var ctorDel = _cacheAccessor.CreateParameterlessConstructor(classType, ctor);

            propertyGetterResults[index] = propGetter;
            fieldGetterResults[index] = fldGetter;
            ctorResults[index] = ctorDel;
        });

        // Verify all returned references are the same as the original ones
        foreach (var result in propertyGetterResults)
        {
            Assert.That(result, Is.SameAs(initialPropGetter), "Property getter delegates should be reference equal to the initial one");
        }

        foreach (var result in fieldGetterResults)
        {
            Assert.That(result, Is.SameAs(initialFldGetter), "Field getter delegates should be reference equal to the initial one");
        }

        foreach (var result in ctorResults)
        {
            Assert.That(result, Is.SameAs(initialCtorDel), "Constructor delegates should be reference equal to the initial one");
        }

        // The call count should not increase during concurrent access since we've already cached the delegates
        Assert.That(_mockAccessor.CallCount, Is.EqualTo(initialCallCount),
            "Cache should prevent additional calls to the underlying accessor during concurrent access");
    }
}