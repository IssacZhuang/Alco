using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class NoInspector : UserControl
{


    public NoInspector()
    {
        InitializeComponent();

    }

    protected override void OnDataContextEndUpdate()
    {
        ViewModels.NoInspector? viewModel = DataContext as ViewModels.NoInspector;
        if (viewModel != null)
        {
            TextHint.Text = viewModel.TextHint;
        }
    }


}