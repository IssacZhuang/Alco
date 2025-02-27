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
            Bind(viewModel);
        }
    }

    private void Bind(ViewModels.PropertyEditorString viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        InputText.Bind(TextBox.TextProperty, new Binding(memberInfo.Name)
        {
            Source = viewModel.Target,
        });

        InputText.TextChanged += (sender, e) =>
        {
            viewModel.Refresh();
        };
    }
}