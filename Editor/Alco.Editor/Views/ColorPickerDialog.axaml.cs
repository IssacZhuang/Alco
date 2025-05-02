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
            // TODO: Handle color selection confirmation
            Close(true);
        }

        private void OnBtnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}