using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyEnumEditor : UserControl
{
    public PropertyEnumEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyEnumEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyEnumEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        // Set the items source to the enum values
        InputComboBox.ItemsSource = viewModel.EnumValues;

        // Bind the selected item to the SelectedValue property
        InputComboBox.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(viewModel.SelectedValue))
        {
            Source = viewModel,
            Mode = BindingMode.TwoWay
        });

        // Handle selection changes
        InputComboBox.SelectionChanged += (sender, e) =>
        {
            viewModel.DoRefreshEvent();
        };

        if (!memberInfo.CanWrite)
        {
            InputComboBox.IsEnabled = false;
        }
    }
}