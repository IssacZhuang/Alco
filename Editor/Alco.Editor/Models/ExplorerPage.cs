using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Alco.Editor.Models
{
    public class ExplorerPage : Page
    {
        public override string IconData => "M20 4C21.1046 4 22 4.89543 22 6L22 18C22 19.1046 21.1046 20 20 20H4C2.89543 20 2 19.1046 2 18V6C2 4.89543 2.89543 4 4 4L20 4ZM20 6L4 6V18H20V6ZM5 8H19V10H5V8ZM5 11H19V13H5V11ZM5 14H13V16H5V14Z";
        public override string Tooltip => "Explorer";

        protected override UserControl CreateContent()
        {
            // 创建Explorer页面的内容
            var content = new DockPanel();

            // 添加工具栏
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10),
                Spacing = 10
            };
            DockPanel.SetDock(toolbar, Dock.Top);

            toolbar.Children.Add(new Button { Content = "New File" });
            toolbar.Children.Add(new Button { Content = "New Folder" });
            toolbar.Children.Add(new Button { Content = "Refresh" });

            content.Children.Add(toolbar);

            // 添加文件树
            var treeView = new TreeView
            {
                Background = new SolidColorBrush(Color.Parse("#252526"))
            };

            content.Children.Add(treeView);

            return new UserControl { Content = content };
        }

        public override void OnActivated()
        {
            base.OnActivated();
            // 在页面激活时刷新文件树等
        }
    }
}