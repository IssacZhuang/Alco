using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyEditorString : UserControl
{
    public PropertyEditorString()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyEditorString viewModel)
        {
            Bind(viewModel.Target, viewModel.MemberInfo);
        }
    }

    public void Bind(object target, AccessMemberInfo memberInfo)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(memberInfo);

        InputText.Bind(TextBox.TextProperty, new Binding(memberInfo.Name)
        {
            Source = target,
        });
    }
}