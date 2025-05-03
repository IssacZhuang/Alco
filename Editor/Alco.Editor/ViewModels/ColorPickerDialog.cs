using System;
using Alco.Graphics;
using Avalonia.Media;
using ColorPicker.Models;

namespace Alco.Editor.ViewModels
{
    public class ColorPickerDialog : ViewModelBase
    {
        public event EventHandler<ColorFloat>? ColorPicked;

        public ColorState ColorState { get; set; }

        public ColorPickerDialog()
        {
            ColorState = new ColorState();
        }

        public void DoColorPicked()
        {
            ColorFloat color = new ColorFloat((float)ColorState.RGB_R, (float)ColorState.RGB_G, (float)ColorState.RGB_B, (float)ColorState.A);
            ColorPicked?.Invoke(this, color);
        }
        
    }
}