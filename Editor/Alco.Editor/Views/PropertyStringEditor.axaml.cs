using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Alco.IO;

namespace Alco.Editor.Views;

/// <summary>
/// A user control for editing string properties with autocomplete functionality using asset file names
/// </summary>
public partial class PropertyStringEditor : UserControl
{
    public PropertyStringEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyStringEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    /// <summary>
    /// Sets up the AutoCompleteBox with asset file names and binds it to the view model
    /// </summary>
    /// <param name="viewModel">The view model to bind to</param>
    private void Setup(ViewModels.PropertyStringEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        // Set up the AutoCompleteBox with asset file names
        var engine = App.Main.Engine;
        var assetSystem = engine.Assets;

        // Set the items source to all file names in the asset system
        InputText.ItemsSource = assetSystem.AllFileNames;

        // Set filter mode to Contains so that typing any part of a filename will show suggestions
        InputText.FilterMode = AutoCompleteFilterMode.Contains;

        // Bind the selected value to the viewModel's Value property
        InputText.Bind(AutoCompleteBox.TextProperty, new Binding(nameof(viewModel.Value))
        {
            Source = viewModel,
        });

        if (!memberInfo.CanWrite)
        {
            InputText.IsEnabled = false;
        }
    }
}