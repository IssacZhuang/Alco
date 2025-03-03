using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class ProjectPage : UserControl
{
    private class TestObject
    {
        public string Name { get; set; } = "Test";
        public int Age { get; } = 10;
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
    }

    private class TestSubObject
    {
        public string Name { get; set; } = "Test";
        public int Age { get; } = 10;
        public bool IsActive { get; set; } = true;
    }

    public ProjectPage()
    {
        InitializeComponent();
        TestObject testObject = new TestObject();
        ObjectPropertiesEditor.DataContext = new ViewModels.ObjectPropertiesEditor(testObject);
    }
}