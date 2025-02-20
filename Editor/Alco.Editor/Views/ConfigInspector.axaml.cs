using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;
using Alco.IO;

namespace Alco.Editor.Views;

public partial class ConfigInspector : UserControl
{

    public ViewModels.ConfigInspector ViewModel => DataContext as ViewModels.ConfigInspector ?? throw new InvalidOperationException("DataContext is not a ViewModels.ConfigInspector");

    public ConfigInspector()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BaseConfig? config = ViewModel.Asset;
        if (config == null)
        {
            return;
        }
        DynamicAccessor accessor = new DynamicAccessor(config.GetType());

        PropertiesEditor.Children.Clear();
        for (int i = 0; i < accessor.PropertyNames.Count; i++)
        {
            PropertiesEditor.Children.Add(new TextBlock { Text = $"{accessor.PropertyNames[i]} [{accessor.PropertyTypes[i].Name}]" });
        }

        //test
        PropertyNumber propertyNumber = new PropertyNumber();
        PropertiesEditor.Children.Add(propertyNumber);
        propertyNumber.Bind(ViewModel, nameof(ViewModel.TestNumber));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TextJsonPreview.Text = ViewModel.SerializedJson;
    }
}