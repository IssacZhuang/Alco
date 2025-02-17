using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Alco.Benchmark;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BenchmarkDynamicAccess
{
    private class TestClass
    {
        public int IntProperty { get; set; }
        public string StringProperty { get; set; }
        public float FloatField;
        public object ObjectField;

        public TestClass()
        {
            IntProperty = 42;
            StringProperty = "Hello World";
            FloatField = 3.14f;
            ObjectField = new object();
        }
    }

    private TestClass _instance;
    private PropertyInfo _intPropertyInfo;
    private PropertyInfo _stringPropertyInfo;
    private FieldInfo _floatFieldInfo;
    private FieldInfo _objectFieldInfo;
    private DynamicAccessor _accessor;

    [GlobalSetup]
    public void Setup()
    {
        _instance = new TestClass();
        var type = typeof(TestClass);
        _intPropertyInfo = type.GetProperty(nameof(TestClass.IntProperty));
        _stringPropertyInfo = type.GetProperty(nameof(TestClass.StringProperty));
        _floatFieldInfo = type.GetField(nameof(TestClass.FloatField));
        _objectFieldInfo = type.GetField(nameof(TestClass.ObjectField));
        _accessor = new DynamicAccessor(type);
    }

    [Benchmark(Description = "Get Int Property - DynamicAccessor")]
    public int GetIntPropertyDynamic()
    {
        return (int)_accessor.GetValue(_instance, nameof(TestClass.IntProperty));
    }

    [Benchmark(Description = "Get Int Property - Reflection")]
    public int GetIntPropertyReflection()
    {
        return (int)_intPropertyInfo.GetValue(_instance);
    }

    [Benchmark(Description = "Get String Property - DynamicAccessor")]
    public string GetStringPropertyDynamic()
    {
        return (string)_accessor.GetValue(_instance, nameof(TestClass.StringProperty));
    }

    [Benchmark(Description = "Get String Property - Reflection")]
    public string GetStringPropertyReflection()
    {
        return (string)_stringPropertyInfo.GetValue(_instance);
    }

    [Benchmark(Description = "Get Float Field - DynamicAccessor")]
    public float GetFloatFieldDynamic()
    {
        return (float)_accessor.GetValue(_instance, nameof(TestClass.FloatField));
    }

    [Benchmark(Description = "Get Float Field - Reflection")]
    public float GetFloatFieldReflection()
    {
        return (float)_floatFieldInfo.GetValue(_instance);
    }

    [Benchmark(Description = "Get Object Field - DynamicAccessor")]
    public object GetObjectFieldDynamic()
    {
        return _accessor.GetValue(_instance, nameof(TestClass.ObjectField));
    }

    [Benchmark(Description = "Get Object Field - Reflection")]
    public object GetObjectFieldReflection()
    {
        return _objectFieldInfo.GetValue(_instance);
    }

    [Benchmark(Description = "Set Int Property - DynamicAccessor")]
    public void SetIntPropertyDynamic()
    {
        _accessor.SetValue(_instance, nameof(TestClass.IntProperty), 100);
    }

    [Benchmark(Description = "Set Int Property - Reflection")]
    public void SetIntPropertyReflection()
    {
        _intPropertyInfo.SetValue(_instance, 100);
    }

    [Benchmark(Description = "Set String Property - DynamicAccessor")]
    public void SetStringPropertyDynamic()
    {
        _accessor.SetValue(_instance, nameof(TestClass.StringProperty), "New Value");
    }

    [Benchmark(Description = "Set String Property - Reflection")]
    public void SetStringPropertyReflection()
    {
        _stringPropertyInfo.SetValue(_instance, "New Value");
    }

    [Benchmark(Description = "Set Float Field - DynamicAccessor")]
    public void SetFloatFieldDynamic()
    {
        _accessor.SetValue(_instance, nameof(TestClass.FloatField), 2.718f);
    }

    [Benchmark(Description = "Set Float Field - Reflection")]
    public void SetFloatFieldReflection()
    {
        _floatFieldInfo.SetValue(_instance, 2.718f);
    }

    [Benchmark(Description = "Set Object Field - DynamicAccessor")]
    public void SetObjectFieldDynamic()
    {
        _accessor.SetValue(_instance, nameof(TestClass.ObjectField), new object());
    }

    [Benchmark(Description = "Set Object Field - Reflection")]
    public void SetObjectFieldReflection()
    {
        _objectFieldInfo.SetValue(_instance, new object());
    }

    [Benchmark(Description = "TryGetValue - DynamicAccessor")]
    public bool TryGetValueDynamic()
    {
        return _accessor.TryGetValue(_instance, nameof(TestClass.IntProperty), out _);
    }

    [Benchmark(Description = "TrySetValue - DynamicAccessor")]
    public bool TrySetValueDynamic()
    {
        return _accessor.TrySetValue(_instance, nameof(TestClass.IntProperty), 100);
    }
}