using System;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Alco.Editor.Views;

public partial class PropertyColorFloatEditor : UserControl
{
    public PropertyColorFloatEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyColorFloatEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyColorFloatEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        // Bind the rectangle's fill to the color in the view model
        BtnColor.Bind(Button.BackgroundProperty, new Binding(nameof(viewModel.ColorBrush))
        {
            Source = viewModel,
        });


        InputR.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.R))
        {
            Source = viewModel,
        });

        InputG.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.G))
        {
            Source = viewModel,
        });

        InputB.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.B))
        {
            Source = viewModel,
        });

        InputA.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.A))
        {
            Source = viewModel,
        });


    }

    private void OnBtnColorClick(object sender, RoutedEventArgs e)
    {
        Views.Editor? editorWindow = App.Main.EditorWindow;
        if (editorWindow == null)
        {
            return;
        }

        if (DataContext is not ViewModels.PropertyColorFloatEditor viewModel)
        {
            return;
        }

        var dialog = new ColorPickerDialog();
        var vm = new ViewModels.ColorPickerDialog();
        dialog.DataContext = vm;
        vm.ColorPicked += (sender, color) =>
        {
            viewModel.R = color.R;
            viewModel.G = color.G;
            viewModel.B = color.B;
            viewModel.A = color.A;
        };

        viewModel.ColorFloat.Decompose(out Color32 color, out float intensity);
        vm.Intensity = intensity;

        dialog.ShowDialog(editorWindow);
        
        dialog.ColorPicker.Color.RGB_R = color.R;
        dialog.ColorPicker.Color.RGB_G = color.G;
        dialog.ColorPicker.Color.RGB_B = color.B;
        dialog.ColorPicker.Color.A = color.A;
        
    }
}