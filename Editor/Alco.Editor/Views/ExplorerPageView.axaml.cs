using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views
{
    public partial class ExplorerPageView : UserControl
    {
        public ExplorerPageView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
} 