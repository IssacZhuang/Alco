// using System.Diagnostics.CodeAnalysis;
// using NUnit.Framework;
// using Alco.IO;
// using System.Text.Json.Serialization;
// using System.Text.Json;
// using System.Reflection;
// using System.Linq;

// namespace Alco.Engine.Test;




// public class TestJson
// {

//     private class TestObject
//     {
//         public Type SelfType;
//         public Type IntType;
//         public string Text;
//     }

//     public class TypeJsonConverter : JsonConverter<Type>
//     {
//         private readonly List<Assembly> assemblies = new();

//         public TypeJsonConverter()
//         {
//             assemblies.Add(Assembly.GetExecutingAssembly());
//             assemblies.Add(Assembly.GetAssembly(typeof(int)));//System.Private.CoreLib
//         }

//         public void AddAssembly(Assembly assembly)
//         {
//             assemblies.Add(assembly);
//         }

//         public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//         {
//             var typeName = reader.GetString();
//             if (string.IsNullOrEmpty(typeName)) return null;

//             // First try direct Type.GetType
//             var type = Type.GetType(typeName);
//             if (type != null) return type;

//             // Try to find in registered assemblies
//             foreach (var assembly in assemblies)
//             {
//                 // Try to find type by full name or namespace.class format
//                 type = assembly.GetTypes().FirstOrDefault(t =>
//                     t.FullName == typeName || // Try full name
//                     $"{t.Namespace}.{t.Name}" == typeName); // Try namespace.class format

//                 if (type != null) return type;
//             }

//             throw new JsonException($"Could not resolve type: {typeName}");
//         }

//         public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
//         {
//             if (value == null)
//             {
//                 writer.WriteNullValue();
//                 return;
//             }
//             // Write in namespace.class format
//             writer.WriteStringValue($"{value.Namespace}.{value.Name}");
//         }
//     }

//     [Test]
//     public void TestType()
//     {
//         var options = new JsonSerializerOptions();
//         options.Converters.Add(new TypeJsonConverter());

//         string typeTestObject = $"{typeof(TestObject).Namespace}.{typeof(TestObject).Name}";
//         string typeInt = "System.Int32";

//         string json = $@"
//         {{
//             ""SelfType"": ""{typeTestObject}"",
//             ""IntType"": ""{typeInt}"",
//             ""Text"": ""Hello, World!""
//         }}
//         ";

//         TestContext.WriteLine(json);

//         var testObject = JsonSerializer.Deserialize<TestObject>(json, options);
//         Assert.That(testObject.SelfType, Is.EqualTo(typeof(TestObject)));
//         Assert.That(testObject.IntType, Is.EqualTo(typeof(int)));
//         Assert.That(testObject.Text, Is.EqualTo("Hello, World!"));
//     }
// }

