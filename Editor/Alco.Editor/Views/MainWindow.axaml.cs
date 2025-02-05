using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia;

namespace Alco.Editor.Views
{
    public partial class MainWindow : Window
    {
        // Vector graphics data
        private static readonly string DocumentIcon = "M20 4C21.1046 4 22 4.89543 22 6L22 18C22 19.1046 21.1046 20 20 20H4C2.89543 20 2 19.1046 2 18V6C2 4.89543 2.89543 4 4 4L20 4ZM20 6L4 6V18H20V6ZM5 8H19V10H5V8ZM5 11H19V13H5V11ZM5 14H13V16H5V14Z";
        private static readonly string SearchIcon = "M10 2.5C14.1421 2.5 17.5 5.85786 17.5 10C17.5 11.7101 16.9276 13.2866 15.9536 14.5483L20.7071 19.2929C21.0976 19.6834 21.0976 20.3166 20.7071 20.7071C20.3166 21.0976 19.6834 21.0976 19.2929 20.7071L14.5483 15.9536C13.2866 16.9276 11.7101 17.5 10 17.5C5.85786 17.5 2.5 14.1421 2.5 10C2.5 5.85786 5.85786 2.5 10 2.5ZM10 4.5C6.96243 4.5 4.5 6.96243 4.5 10C4.5 13.0376 6.96243 15.5 10 15.5C13.0376 15.5 15.5 13.0376 15.5 10C15.5 6.96243 13.0376 4.5 10 4.5Z";
        private static readonly string BranchIcon = "M16 12.5C17.3807 12.5 18.5 13.6193 18.5 15C18.5 16.3807 17.3807 17.5 16 17.5C14.6193 17.5 13.5 16.3807 13.5 15C13.5 13.6193 14.6193 12.5 16 12.5ZM8 6.5C9.38071 6.5 10.5 7.61929 10.5 9C10.5 10.3807 9.38071 11.5 8 11.5C6.61929 11.5 5.5 10.3807 5.5 9C5.5 7.61929 6.61929 6.5 8 6.5ZM16 14.5C15.7239 14.5 15.5 14.7239 15.5 15C15.5 15.2761 15.7239 15.5 16 15.5C16.2761 15.5 16.5 15.2761 16.5 15C16.5 14.7239 16.2761 14.5 16 14.5ZM8 8.5C7.72386 8.5 7.5 8.72386 7.5 9C7.5 9.27614 7.72386 9.5 8 9.5C8.27614 9.5 8.5 9.27614 8.5 9C8.5 8.72386 8.27614 8.5 8 8.5Z";
        private static readonly string ExtensionIcon = "M21 6.5C21 8.43 19.43 10 17.5 10C15.57 10 14 8.43 14 6.5C14 4.57 15.57 3 17.5 3C19.43 3 21 4.57 21 6.5ZM19 6.5C19 5.67 18.33 5 17.5 5C16.67 5 16 5.67 16 6.5C16 7.33 16.67 8 17.5 8C18.33 8 19 7.33 19 6.5ZM10.5 21C8.57 21 7 19.43 7 17.5C7 15.57 8.57 14 10.5 14C12.43 14 14 15.57 14 17.5C14 19.43 12.43 21 10.5 21ZM10.5 16C9.67 16 9 16.67 9 17.5C9 18.33 9.67 19 10.5 19C11.33 19 12 18.33 12 17.5C12 16.67 11.33 16 10.5 16ZM6.5 10C4.57 10 3 8.43 3 6.5C3 4.57 4.57 3 6.5 3C8.43 3 10 4.57 10 6.5C10 8.43 8.43 10 6.5 10ZM6.5 5C5.67 5 5 5.67 5 6.5C5 7.33 5.67 8 6.5 8C7.33 8 8 7.33 8 6.5C8 5.67 7.33 5 6.5 5Z";

        public MainWindow()
        {
            InitializeComponent();
            InitializeMenu();
            InitializeActivityBar();
        }

        private void InitializeActivityBar()
        {
            var iconButtons = new (string data, string tooltip)[]
            {
                (DocumentIcon, "Explorer"),
                (SearchIcon, "Search"),
                (BranchIcon, "Source Control"),
                (ExtensionIcon, "Extensions")
            };

            foreach (var (data, tooltip) in iconButtons)
            {
                var button = new Button
                {
                    Width = 48,
                    Height = 48,
                    Margin = new Thickness(0, 5),
                    Background = Brushes.Transparent
                };

                ToolTip.SetTip(button, tooltip);

                var pathIcon = new PathIcon
                {
                    Data = StreamGeometry.Parse(data),
                    Foreground = new SolidColorBrush(Color.Parse("#D1D1D1"))
                };

                button.Content = pathIcon;
                ActivityBar.Children.Add(button);
            }
        }

        private void InitializeMenu()
        {
            // File Menu
            var fileMenu = new MenuItem { Header = "_File" };
            fileMenu.Items.Add(new MenuItem { Header = "_New" });
            fileMenu.Items.Add(new MenuItem { Header = "_Open..." });
            fileMenu.Items.Add(new MenuItem { Header = "_Save" });
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(new MenuItem { Header = "_Exit" });

            // Edit Menu
            var editMenu = new MenuItem { Header = "_Edit" };
            editMenu.Items.Add(new MenuItem { Header = "_Undo" });
            editMenu.Items.Add(new MenuItem { Header = "_Redo" });
            editMenu.Items.Add(new Separator());
            editMenu.Items.Add(new MenuItem { Header = "Cu_t" });
            editMenu.Items.Add(new MenuItem { Header = "_Copy" });
            editMenu.Items.Add(new MenuItem { Header = "_Paste" });

            // View Menu
            var viewMenu = new MenuItem { Header = "_View" };
            viewMenu.Items.Add(new MenuItem { Header = "_Explorer" });
            viewMenu.Items.Add(new MenuItem { Header = "_Search" });
            viewMenu.Items.Add(new MenuItem { Header = "_Extensions" });

            // Add all menus to the main menu
            MainMenu.Items.Add(fileMenu);
            MainMenu.Items.Add(editMenu);
            MainMenu.Items.Add(viewMenu);
        }
    }
}