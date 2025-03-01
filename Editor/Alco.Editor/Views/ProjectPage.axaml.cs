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
        public Vector2 Position { get; set; } = new Vector2(10, 20);
        public Vector3 Position3 { get; set; } = new Vector3(10, 20, 30);
        public Vector4 Position4 { get; set; } = new Vector4(10, 20, 30, 40);
    }

    public ProjectPage()
    {
        InitializeComponent();
        TestObject testObject = new TestObject();
        ObjectPropertiesEditor.DataContext = new ViewModels.ObjectPropertiesEditor(testObject);
    }
}