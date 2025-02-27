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
    }

    public ProjectPage()
    {
        InitializeComponent();
        TestObject testObject = new TestObject();
        ObjectPropertiesEditor.DataContext = new ViewModels.ObjectPropertiesEditor(testObject);
    }
}