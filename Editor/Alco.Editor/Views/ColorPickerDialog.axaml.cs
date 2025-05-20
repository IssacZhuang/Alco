using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Alco.Editor.Views
{
    public partial class ColorPickerDialog : Window
    {
        public ColorPickerDialog()
        {
            InitializeComponent();
        }

        private void OnBtnConfirmClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ColorPickerDialog viewModel)
            {
                viewModel.DoColorPicked();
            }
            Close(true);
        }

        private void OnBtnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}