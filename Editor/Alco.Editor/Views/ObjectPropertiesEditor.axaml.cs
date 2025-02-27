using System;
using System.Text.Json;
using Alco.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class ObjectPropertiesEditor : UserControl
{
    public ObjectPropertiesEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.ObjectPropertiesEditor viewModel)
        {
            Setup(viewModel);
        }
        else
        {
            Clear();
        }
    }

    private void Setup(ViewModels.ObjectPropertiesEditor viewModel)
    {
        Root.Children.Clear();

        foreach (var member in viewModel.AccessTypeInfo.Members)
        {
            TextBlock textBlock = new TextBlock();
            textBlock.Text = member.Name;
            Root.Children.Add(textBlock);

            PropertyEditor propertyEditor = PropertyEditor.CreatePropertyEditor(viewModel.Target, member);
            Control control = propertyEditor.CreateControl();
            Root.Children.Add(control);
        }
    }


    private void Clear()
    {

    }
}