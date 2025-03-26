using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Alco.IO;
using Avalonia.Input;
using Avalonia.Threading;
using Alco.Rendering;
using Alco.Graphics;

namespace Alco.Editor.Views;

public partial class InspectorForTexture : UserControl
{
    public InspectorForTexture()
    {
        InitializeComponent();
        InitializeBlendStateComboBox();
    }

    private void InitializeBlendStateComboBox()
    {
        BlendStateComboBox.Items.Add("Opaque");
        BlendStateComboBox.Items.Add("AlphaBlend");
        BlendStateComboBox.Items.Add("Additive");
        BlendStateComboBox.Items.Add("PremultipliedAlpha");
        BlendStateComboBox.Items.Add("NonPremultipliedAlpha");
        BlendStateComboBox.SelectedIndex = 1; // Default to AlphaBlend
    }

    public void OnBlendStateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SurfaceView == null)
            return;

        string? selectedBlendState = BlendStateComboBox.SelectedItem as string;

        switch (selectedBlendState)
        {
            case "Opaque":
                SurfaceView.BlendState = BlendState.Opaque;
                break;
            case "AlphaBlend":
                SurfaceView.BlendState = BlendState.AlphaBlend;
                break;
            case "Additive":
                SurfaceView.BlendState = BlendState.Additive;
                break;
            case "PremultipliedAlpha":
                SurfaceView.BlendState = BlendState.PremultipliedAlpha;
                break;
            case "NonPremultipliedAlpha":
                SurfaceView.BlendState = BlendState.NonPremultipliedAlpha;
                break;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ViewModels.InspectorForTexture viewModel)
        {
            return;
        }

        Texture2D? texture = viewModel.Asset;
        if (texture == null)
        {
            return;
        }

        SurfaceView.Texture = texture;

        Refresh();
    }

    private void Refresh()
    {
        if (DataContext is not ViewModels.InspectorForTexture viewModel)
        {
            return;
        }

        TextFilename.Text = viewModel.Filename + " " + (viewModel.IsModified ? "*" : "");
    }


}