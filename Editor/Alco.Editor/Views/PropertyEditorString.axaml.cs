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
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyEditorString viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        InputText.Bind(TextBox.TextProperty, new Binding(memberInfo.Name)
        {
            Source = viewModel.Target,
        });

        if (memberInfo.CanWrite)
        {
            InputText.TextChanged += (sender, e) =>
            {
                viewModel.Refresh();
            };
        }
        else
        {
            InputText.IsReadOnly = true;
        }


    }
}