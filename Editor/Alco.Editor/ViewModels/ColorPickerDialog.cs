using System;
using Alco.Graphics;
using Avalonia.Media;
using ColorPicker.Models;

namespace Alco.Editor.ViewModels
{
    public class ColorPickerDialog : ViewModelBase
    {
        private float _intensity = 1;

        public event EventHandler<ColorFloat>? ColorPicked;

        public ColorState ColorState { get; set; }

        public float Intensity { get => _intensity; set => SetProperty(ref _intensity, value); }
        public float MinIntensity { get; set; } = 1;
        public float MaxIntensity { get; set; } = 10;

        public ColorPickerDialog()
        {
            ColorState = new ColorState();
        }

        public void DoColorPicked()
        {
            ColorFloat color = new ColorFloat((float)ColorState.RGB_R, (float)ColorState.RGB_G, (float)ColorState.RGB_B, (float)ColorState.A);
            color.R *= Intensity;
            color.G *= Intensity;
            color.B *= Intensity;
            ColorPicked?.Invoke(this, color);
        }
        
    }
}