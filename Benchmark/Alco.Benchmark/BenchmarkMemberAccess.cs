using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using System.Linq;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks reflection and Reflection.Emit based member access.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BenchmarkMemberAccess
{
    /// <summary>
    /// Provides the members accessed by the benchmarks.
    /// </summary>
    public class TestReflectionClass
    {
        /// <summary>
        /// Gets or sets the integer property under test.
        /// </summary>
        public int IntProperty { get; set; }
        /// <summary>
        /// Gets or sets the string property under test.
        /// </summary>
        public string StringProperty { get; set; }
        public float FloatField;
        public object ObjectField;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestReflectionClass"/> class with default values.
        /// </summary>
        public TestReflectionClass()
        {
            IntProperty = 42;
            StringProperty = "Hello World";
            FloatField = 3.14f;
            ObjectField = new object();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestReflectionClass"/> class with the specified property values.
        /// </summary>
        /// <param name="intValue">The initial value of <see cref="IntProperty"/>.</param>
        /// <param name="strValue">The initial value of <see cref="StringProperty"/>.</param>
        public TestReflectionClass(int intValue, string strValue)
        {
            IntProperty = intValue;
            StringProperty = strValue;
        }
    }

    private TestReflectionClass _instance;
    private Type _type;
    private ConstructorInfo _ctor;
    private PropertyInfo _intPropertyInfo;
    private PropertyInfo _stringPropertyInfo;
    private FieldInfo _floatFieldInfo;
    private FieldInfo _objectFieldInfo;
    private ReflectionMemberAccessor _reflectionAccessor;
    private ReflectionEmitMemberAccessor _reflectionEmitAccessor;

    // Cached delegates for property and field access
    private Func<object, int> _reflectionIntGetter;
    private Func<object, int> _reflectionEmitIntGetter;
    private Action<object, int> _reflectionIntSetter;
    private Action<object, int> _reflectionEmitIntSetter;
    private Func<object, string> _reflectionStringGetter;
    private Func<object, string> _reflectionEmitStringGetter;
    private Action<object, string> _reflectionStringSetter;
    private Action<object, string> _reflectionEmitStringSetter;
    private Func<object, float> _reflectionFloatGetter;
    private Func<object, float> _reflectionEmitFloatGetter;
    private Action<object, float> _reflectionFloatSetter;
    private Action<object, float> _reflectionEmitFloatSetter;
    private Func<object, object> _reflectionObjectGetter;
    private Func<object, object> _reflectionEmitObjectGetter;
    private Action<object, object> _reflectionObjectSetter;
    private Action<object, object> _reflectionEmitObjectSetter;

    // Cached delegates for constructor
    private Func<object> _reflectionCtor;
    private Func<object> _reflectionEmitCtor;
    private Func<object[], TestReflectionClass> _reflectionParamCtor;
    private Func<object[], TestReflectionClass> _reflectionEmitParamCtor;
    private ParameterizedConstructorDelegate<TestReflectionClass, int, string, object, object> _reflectionParamCtorDelegate;
    private ParameterizedConstructorDelegate<TestReflectionClass, int, string, object, object> _reflectionEmitParamCtorDelegate;

    // AccessTypeInfo instances
    private AccessTypeInfo _reflectionAccessTypeInfo;
    private AccessTypeInfo _reflectionEmitAccessTypeInfo;

    // Cached AccessMemberInfo references
    private AccessMemberInfo _reflectionIntPropertyMember;
    private AccessMemberInfo _reflectionStringPropertyMember;
    private AccessMemberInfo _reflectionFloatFieldMember;
    private AccessMemberInfo _reflectionEmitIntPropertyMember;
    private AccessMemberInfo _reflectionEmitStringPropertyMember;
    private AccessMemberInfo _reflectionEmitFloatFieldMember;

    /// <summary>
    /// Initializes the accessors, delegates, and cached member information used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _instance = new TestReflectionClass();
        _type = typeof(TestReflectionClass);
        _ctor = _type.GetConstructor(new[] { typeof(int), typeof(string) })!;
        _intPropertyInfo = _type.GetProperty(nameof(TestReflectionClass.IntProperty))!;
        _stringPropertyInfo = _type.GetProperty(nameof(TestReflectionClass.StringProperty))!;
        _floatFieldInfo = _type.GetField(nameof(TestReflectionClass.FloatField))!;
        _objectFieldInfo = _type.GetField(nameof(TestReflectionClass.ObjectField))!;

        _reflectionAccessor = new ReflectionMemberAccessor();
        _reflectionEmitAccessor = new ReflectionEmitMemberAccessor();

        // Initialize property and field delegates
        _reflectionIntGetter = _reflectionAccessor.CreatePropertyGetter<int>(_intPropertyInfo);
        _reflectionEmitIntGetter = _reflectionEmitAccessor.CreatePropertyGetter<int>(_intPropertyInfo);
        _reflectionIntSetter = _reflectionAccessor.CreatePropertySetter<int>(_intPropertyInfo);
        _reflectionEmitIntSetter = _reflectionEmitAccessor.CreatePropertySetter<int>(_intPropertyInfo);

        _reflectionStringGetter = _reflectionAccessor.CreatePropertyGetter<string>(_stringPropertyInfo);
        _reflectionEmitStringGetter = _reflectionEmitAccessor.CreatePropertyGetter<string>(_stringPropertyInfo);
        _reflectionStringSetter = _reflectionAccessor.CreatePropertySetter<string>(_stringPropertyInfo);
        _reflectionEmitStringSetter = _reflectionEmitAccessor.CreatePropertySetter<string>(_stringPropertyInfo);

        _reflectionFloatGetter = _reflectionAccessor.CreateFieldGetter<float>(_floatFieldInfo);
        _reflectionEmitFloatGetter = _reflectionEmitAccessor.CreateFieldGetter<float>(_floatFieldInfo);
        _reflectionFloatSetter = _reflectionAccessor.CreateFieldSetter<float>(_floatFieldInfo);
        _reflectionEmitFloatSetter = _reflectionEmitAccessor.CreateFieldSetter<float>(_floatFieldInfo);

        _reflectionObjectGetter = _reflectionAccessor.CreateFieldGetter<object>(_objectFieldInfo);
        _reflectionEmitObjectGetter = _reflectionEmitAccessor.CreateFieldGetter<object>(_objectFieldInfo);
        _reflectionObjectSetter = _reflectionAccessor.CreateFieldSetter<object>(_objectFieldInfo);
        _reflectionEmitObjectSetter = _reflectionEmitAccessor.CreateFieldSetter<object>(_objectFieldInfo);

        // Initialize constructor delegates
        _reflectionCtor = _reflectionAccessor.CreateParameterlessConstructor(_type, _type.GetConstructor(Type.EmptyTypes))!;
        _reflectionEmitCtor = _reflectionEmitAccessor.CreateParameterlessConstructor(_type, _type.GetConstructor(Type.EmptyTypes))!;
        _reflectionParamCtor = _reflectionAccessor.CreateParameterizedConstructor<TestReflectionClass>(_ctor);
        _reflectionEmitParamCtor = _reflectionEmitAccessor.CreateParameterizedConstructor<TestReflectionClass>(_ctor);
        _reflectionParamCtorDelegate = _reflectionAccessor.CreateParameterizedConstructor<TestReflectionClass, int, string, object, object>(_ctor)!;
        _reflectionEmitParamCtorDelegate = _reflectionEmitAccessor.CreateParameterizedConstructor<TestReflectionClass, int, string, object, object>(_ctor)!;

        // Initialize AccessTypeInfo instances
        _reflectionAccessTypeInfo = new AccessTypeInfo(_type, _reflectionAccessor);
        _reflectionEmitAccessTypeInfo = new AccessTypeInfo(_type, _reflectionEmitAccessor);

        // Cache AccessMemberInfo references
        _reflectionIntPropertyMember = _reflectionAccessTypeInfo.Members.First(m => m.Name == nameof(TestReflectionClass.IntProperty));
        _reflectionStringPropertyMember = _reflectionAccessTypeInfo.Members.First(m => m.Name == nameof(TestReflectionClass.StringProperty));
        _reflectionFloatFieldMember = _reflectionAccessTypeInfo.Members.First(m => m.Name == nameof(TestReflectionClass.FloatField));
        _reflectionEmitIntPropertyMember = _reflectionEmitAccessTypeInfo.Members.First(m => m.Name == nameof(TestReflectionClass.IntProperty));
        _reflectionEmitStringPropertyMember = _reflectionEmitAccessTypeInfo.Members.First(m => m.Name == nameof(TestReflectionClass.StringProperty));
        _reflectionEmitFloatFieldMember = _reflectionEmitAccessTypeInfo.Members.First(m => m.Name == nameof(TestReflectionClass.FloatField));
    }

    /// <summary>
    /// Benchmarks creating an instance via reflection.
    /// </summary>
    [Benchmark(Description = "Create Instance - Reflection")]
    public object CreateInstanceReflection() => _reflectionCtor();

    /// <summary>
    /// Benchmarks creating an instance via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Create Instance - ReflectionEmit")]
    public object CreateInstanceReflectionEmit() => _reflectionEmitCtor();

    /// <summary>
    /// Benchmarks creating an instance with constructor arguments via reflection.
    /// </summary>
    [Benchmark(Description = "Create Instance with Params - Reflection")]
    public TestReflectionClass CreateInstanceWithParamsReflection() => _reflectionParamCtor(new object[] { 100, "Test" });

    /// <summary>
    /// Benchmarks creating an instance with constructor arguments via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Create Instance with Params - ReflectionEmit")]
    public TestReflectionClass CreateInstanceWithParamsReflectionEmit() => _reflectionEmitParamCtor(new object[] { 100, "Test" });

    /// <summary>
    /// Benchmarks creating an instance through a delegate prepared with reflection.
    /// </summary>
    [Benchmark(Description = "Create Instance with Delegate - Reflection")]
    public TestReflectionClass CreateInstanceWithDelegateReflection() => _reflectionParamCtorDelegate(100, "Test", null, null);

    /// <summary>
    /// Benchmarks creating an instance through a delegate prepared with Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Create Instance with Delegate - ReflectionEmit")]
    public TestReflectionClass CreateInstanceWithDelegateReflectionEmit() => _reflectionEmitParamCtorDelegate(100, "Test", null, null);

    /// <summary>
    /// Benchmarks reading an integer property via reflection.
    /// </summary>
    [Benchmark(Description = "Get Int Property - Reflection")]
    public int GetIntPropertyReflection() => _reflectionIntGetter(_instance);

    /// <summary>
    /// Benchmarks reading an integer property via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get Int Property - ReflectionEmit")]
    public int GetIntPropertyReflectionEmit() => _reflectionEmitIntGetter(_instance);

    /// <summary>
    /// Benchmarks writing an integer property via reflection.
    /// </summary>
    [Benchmark(Description = "Set Int Property - Reflection")]
    public void SetIntPropertyReflection() => _reflectionIntSetter(_instance, 100);

    /// <summary>
    /// Benchmarks writing an integer property via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set Int Property - ReflectionEmit")]
    public void SetIntPropertyReflectionEmit() => _reflectionEmitIntSetter(_instance, 100);

    /// <summary>
    /// Benchmarks reading a string property via reflection.
    /// </summary>
    [Benchmark(Description = "Get String Property - Reflection")]
    public string GetStringPropertyReflection() => _reflectionStringGetter(_instance);

    /// <summary>
    /// Benchmarks reading a string property via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get String Property - ReflectionEmit")]
    public string GetStringPropertyReflectionEmit() => _reflectionEmitStringGetter(_instance);

    /// <summary>
    /// Benchmarks writing a string property via reflection.
    /// </summary>
    [Benchmark(Description = "Set String Property - Reflection")]
    public void SetStringPropertyReflection() => _reflectionStringSetter(_instance, "New Value");

    /// <summary>
    /// Benchmarks writing a string property via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set String Property - ReflectionEmit")]
    public void SetStringPropertyReflectionEmit() => _reflectionEmitStringSetter(_instance, "New Value");

    /// <summary>
    /// Benchmarks reading a float field via reflection.
    /// </summary>
    [Benchmark(Description = "Get Float Field - Reflection")]
    public float GetFloatFieldReflection() => _reflectionFloatGetter(_instance);

    /// <summary>
    /// Benchmarks reading a float field via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get Float Field - ReflectionEmit")]
    public float GetFloatFieldReflectionEmit() => _reflectionEmitFloatGetter(_instance);

    /// <summary>
    /// Benchmarks writing a float field via reflection.
    /// </summary>
    [Benchmark(Description = "Set Float Field - Reflection")]
    public void SetFloatFieldReflection() => _reflectionFloatSetter(_instance, 2.718f);

    /// <summary>
    /// Benchmarks writing a float field via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set Float Field - ReflectionEmit")]
    public void SetFloatFieldReflectionEmit() => _reflectionEmitFloatSetter(_instance, 2.718f);

    /// <summary>
    /// Benchmarks reading an object field via reflection.
    /// </summary>
    [Benchmark(Description = "Get Object Field - Reflection")]
    public object GetObjectFieldReflection() => _reflectionObjectGetter(_instance);

    /// <summary>
    /// Benchmarks reading an object field via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get Object Field - ReflectionEmit")]
    public object GetObjectFieldReflectionEmit() => _reflectionEmitObjectGetter(_instance);

    /// <summary>
    /// Benchmarks writing an object field via reflection.
    /// </summary>
    [Benchmark(Description = "Set Object Field - Reflection")]
    public void SetObjectFieldReflection() => _reflectionObjectSetter(_instance, new object());

    /// <summary>
    /// Benchmarks writing an object field via Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set Object Field - ReflectionEmit")]
    public void SetObjectFieldReflectionEmit() => _reflectionEmitObjectSetter(_instance, new object());

    // AccessTypeInfo benchmarks
    /// <summary>
    /// Benchmarks creating an <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Create AccessTypeInfo - Reflection")]
    public AccessTypeInfo CreateAccessTypeInfoReflection() => new AccessTypeInfo(_type, _reflectionAccessor);

    /// <summary>
    /// Benchmarks creating an <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Create AccessTypeInfo - ReflectionEmit")]
    public AccessTypeInfo CreateAccessTypeInfoReflectionEmit() => new AccessTypeInfo(_type, _reflectionEmitAccessor);

    /// <summary>
    /// Benchmarks reading an integer property through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Get Int Property via AccessTypeInfo - Reflection")]
    public int GetIntPropertyViaAccessTypeInfoReflection()
    {
        return _reflectionIntPropertyMember.GetValue<int>(_instance);
    }

    /// <summary>
    /// Benchmarks reading an integer property through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get Int Property via AccessTypeInfo - ReflectionEmit")]
    public int GetIntPropertyViaAccessTypeInfoReflectionEmit()
    {
        return _reflectionEmitIntPropertyMember.GetValue<int>(_instance);
    }

    /// <summary>
    /// Benchmarks writing an integer property through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Set Int Property via AccessTypeInfo - Reflection")]
    public void SetIntPropertyViaAccessTypeInfoReflection()
    {
        _reflectionIntPropertyMember.SetValue(_instance, 100);
    }

    /// <summary>
    /// Benchmarks writing an integer property through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set Int Property via AccessTypeInfo - ReflectionEmit")]
    public void SetIntPropertyViaAccessTypeInfoReflectionEmit()
    {
        _reflectionEmitIntPropertyMember.SetValue(_instance, 100);
    }

    /// <summary>
    /// Benchmarks reading a string property through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Get String Property via AccessTypeInfo - Reflection")]
    public string GetStringPropertyViaAccessTypeInfoReflection()
    {
        return _reflectionStringPropertyMember.GetValue<string>(_instance);
    }

    /// <summary>
    /// Benchmarks reading a string property through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get String Property via AccessTypeInfo - ReflectionEmit")]
    public string GetStringPropertyViaAccessTypeInfoReflectionEmit()
    {
        return _reflectionEmitStringPropertyMember.GetValue<string>(_instance);
    }

    /// <summary>
    /// Benchmarks writing a string property through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Set String Property via AccessTypeInfo - Reflection")]
    public void SetStringPropertyViaAccessTypeInfoReflection()
    {
        _reflectionStringPropertyMember.SetValue(_instance, "New Value");
    }

    /// <summary>
    /// Benchmarks writing a string property through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set String Property via AccessTypeInfo - ReflectionEmit")]
    public void SetStringPropertyViaAccessTypeInfoReflectionEmit()
    {
        _reflectionEmitStringPropertyMember.SetValue(_instance, "New Value");
    }

    /// <summary>
    /// Benchmarks reading a float field through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Get Float Field via AccessTypeInfo - Reflection")]
    public float GetFloatFieldViaAccessTypeInfoReflection()
    {
        return _reflectionFloatFieldMember.GetValue<float>(_instance);
    }

    /// <summary>
    /// Benchmarks reading a float field through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Get Float Field via AccessTypeInfo - ReflectionEmit")]
    public float GetFloatFieldViaAccessTypeInfoReflectionEmit()
    {
        return _reflectionEmitFloatFieldMember.GetValue<float>(_instance);
    }

    /// <summary>
    /// Benchmarks writing a float field through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Set Float Field via AccessTypeInfo - Reflection")]
    public void SetFloatFieldViaAccessTypeInfoReflection()
    {
        _reflectionFloatFieldMember.SetValue(_instance, 2.718f);
    }

    /// <summary>
    /// Benchmarks writing a float field through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Set Float Field via AccessTypeInfo - ReflectionEmit")]
    public void SetFloatFieldViaAccessTypeInfoReflectionEmit()
    {
        _reflectionEmitFloatFieldMember.SetValue(_instance, 2.718f);
    }

    /// <summary>
    /// Benchmarks creating an instance through <see cref="AccessTypeInfo"/> backed by reflection.
    /// </summary>
    [Benchmark(Description = "Create Instance via AccessTypeInfo - Reflection")]
    public TestReflectionClass CreateInstanceViaAccessTypeInfoReflection() => _reflectionAccessTypeInfo.CreateInstance<TestReflectionClass>();

    /// <summary>
    /// Benchmarks creating an instance through <see cref="AccessTypeInfo"/> backed by Reflection.Emit.
    /// </summary>
    [Benchmark(Description = "Create Instance via AccessTypeInfo - ReflectionEmit")]
    public TestReflectionClass CreateInstanceViaAccessTypeInfoReflectionEmit() => _reflectionEmitAccessTypeInfo.CreateInstance<TestReflectionClass>();
}