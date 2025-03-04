using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Alco.Engine;
using Alco.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class ProjectPage : UserControl
{
    private class TestObject
    {
        public string Name { get; set; } = "Test";
        public int IntValue { get; set; } = 10;
        public uint UintValue { get; set; } = 10;
        public long LongValue { get; set; } = 10;
        public ulong UlongValue { get; set; } = 10;
        public short ShortValue { get; set; } = 10;
        public ushort UshortValue { get; set; } = 10;
        public byte ByteValue { get; set; } = 10;
        public sbyte SbyteValue { get; set; } = 10;
        public float FloatValue { get; set; } = 10;
        public double DoubleValue { get; set; } = 10;
        public Half HalfValue { get; set; } = (Half)10;
        public decimal DecimalValue { get; set; } = 10;
        public bool IsActive { get; set; } = true;
        public Vector2 PositionFloat2 { get; set; } = new Vector2(10, 20);
        public int2 PositionInt2 { get; set; } = new int2(10, 20);
        public uint2 PositionUint2 { get; set; } = new uint2(10, 20);
        public Half2 PositionHalf2 { get; set; } = new Half2(10, 20);
        public Vector3 PositionFloat3 { get; set; } = new Vector3(10, 20, 30);
        public int3 PositionInt3 { get; set; } = new int3(10, 20, 30);
        public uint3 PositionUint3 { get; set; } = new uint3(10, 20, 30);
        public Half3 PositionHalf3 { get; set; } = new Half3(10, 20, 30);
        public Vector4 PositionFloat4 { get; set; } = new Vector4(10, 20, 30, 40);
        public int4 PositionInt4 { get; set; } = new int4(10, 20, 30, 40);
        public uint4 PositionUint4 { get; set; } = new uint4(10, 20, 30, 40);
        public Half4 PositionHalf4 { get; set; } = new Half4(10, 20, 30, 40);
        public TestSubObject SubObject { get; set; } = new TestSubObject();
        public List<int> IntList { get; set; } = new List<int> { 1, 2, 3, 4, 5 };
    }

    private class TestSubObject
    {
        public string Name { get; set; } = "Test";
        public int Age { get; } = 10;
        public bool IsActive { get; set; } = true;
        public TestSubSubObject SubSubObject { get; set; } = new TestSubSubObject();
    }

    private class TestSubSubObject
    {
        public string Name { get; set; } = "Test";
        public int Age { get; } = 10;
        public bool IsActive { get; set; } = true;
    }

    private readonly TestObject _testObject = new TestObject();
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ProjectPage()
    {
        InitializeComponent();
        var objectPropertiesEditor = new ViewModels.ObjectPropertiesEditor(_testObject, "Test Object");
        ObjectPropertiesEditor.DataContext = objectPropertiesEditor;
        objectPropertiesEditor.OnRefresh += () => PrintJson(_testObject);
        var configReferenceResolver = new ConfigReferenceResolver(App.Main.Engine.Assets);
        _jsonSerializerOptions = BaseConfig.BuildJsonSerializerOptions(configReferenceResolver);
        _jsonSerializerOptions.WriteIndented = true;
    }

    private void PrintJson(object obj)
    {

        string json = JsonSerializer.Serialize(obj, _jsonSerializerOptions);
        Console.WriteLine(json);
    }
}