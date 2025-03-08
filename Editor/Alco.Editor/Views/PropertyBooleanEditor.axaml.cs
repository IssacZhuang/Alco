using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyBooleanEditor : UserControl
{
    public PropertyBooleanEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyBooleanEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyBooleanEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        InputCheckBox.Bind(CheckBox.IsCheckedProperty, new Binding(memberInfo.Name)
        {
            Source = viewModel.Target,
        });

        if (memberInfo.CanWrite)
        {
            InputCheckBox.IsCheckedChanged += (sender, e) =>
            {
                viewModel.DoValueChangedEvent();
            };
        }
        else
        {
            InputCheckBox.IsEnabled = false;
        }
    }
} 