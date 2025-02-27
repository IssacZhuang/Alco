using System;
using System.Text;
using System.Text.Json;
using Alco.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class ObjectPropertiesEditor : UserControl
{
    private readonly StringBuilder _builder = new();

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
            TextBlock textBlock = CreateTextBlock(member);
            textBlock.Margin = new Thickness(0, 5);

            Root.Children.Add(textBlock);

            PropertyEditor propertyEditor = PropertyEditor.CreatePropertyEditor(viewModel.Target, member);
            Control control = propertyEditor.CreateControl();
            Root.Children.Add(control);
        }
    }

    private TextBlock CreateTextBlock(AccessMemberInfo member)
    {
        TextBlock textBlock = new TextBlock();
        _builder.Clear();
        _builder.Append(member.Name);
        _builder.Append(" [");
        _builder.Append(member.MemberType.Name);
        _builder.Append(']');
        if (!member.CanWrite)
        {
            _builder.Append(" (Read Only)");
        }
        textBlock.Text = _builder.ToString();
        return textBlock;
    }


    private void Clear()
    {

    }
}