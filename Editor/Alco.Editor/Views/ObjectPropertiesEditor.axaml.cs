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
        TextHeader.Text = GetTitle(viewModel.Header, viewModel.Target.GetType().Name, true);
        Root.Children.Clear();

        foreach ((AccessMemberInfo member, PropertyEditor propertyEditor) in viewModel.PropertyEditors)
        {
            Control control = propertyEditor.CreateControl();

            if (propertyEditor.HasTitle)
            {
                TextBlock textBlock = CreateTextBlock(member);
                textBlock.Margin = new Thickness(0, 5);
                Root.Children.Add(textBlock);
            }
            else
            {
                //just spacing
                UserControl userControl = new UserControl();
                userControl.Margin = new Thickness(0, 5);
                Root.Children.Add(userControl);
            }

            Root.Children.Add(control);
        }
    }

    private TextBlock CreateTextBlock(AccessMemberInfo member)
    {
        TextBlock textBlock = new TextBlock();
        textBlock.Text = GetTitle(member);
        return textBlock;
    }

    private string GetTitle(AccessMemberInfo member)
    {
        return GetTitle(member.Name, member.MemberType.Name, member.CanWrite);
    }

    private string GetTitle(string name, string typeName, bool canWrite)
    {
        _builder.Clear();
        _builder.Append(name);
        _builder.Append(" [");
        _builder.Append(typeName);
        _builder.Append(']');
        if (!canWrite)
        {
            _builder.Append(" (Read Only)");
        }
        return _builder.ToString();
    }

    private void Clear()
    {

    }
}