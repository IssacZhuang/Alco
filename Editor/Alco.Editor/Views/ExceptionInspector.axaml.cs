using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views
{
    public partial class ExceptionInspector : UserControl
    {

        public ExceptionInspector()
        {
            InitializeComponent();

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (DataContext is not ViewModels.ExceptionInspector viewModel)
            {
                return;
            }

            TextExceptionMessage.Text = viewModel.ExceptionMessage;
            TextStackTrace.Text = viewModel.StackTrace;
            TextTitle.Text = viewModel.Title;
        }


    }
}