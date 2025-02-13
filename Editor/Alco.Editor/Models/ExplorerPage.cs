using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Alco.Editor.Views;

namespace Alco.Editor.Models
{
    public class ExplorerPage : Page
    {
        public override string IconData => "M903.253 231.253V682.24L855.467 736H679.253v170.24L625.493 960H174.507l-53.76-53.76V344.747L174.507 288h170.24V120.747L398.507 64H736l167.253 167.253z m-167.253 0h89.6l-89.6-89.6v89.6zM622.507 736h-224l-53.76-53.76V344.747h-170.24v558.507h448V736z m224-448H679.253V120.747H398.507v558.507h448V288z";
        public override string Tooltip => "Explorer";



        protected override UserControl CreateContent()
        {
            var explorerView = new ExplorerPageView(
                new FileEditorMeta(typeof(JsonFileEditor), ".json")
            );
            explorerView.FileEditorCreated += OnFileEditorCreated;
            return explorerView;
        }

        private void OnFileEditorCreated(object? sender, FileEditor editor)
        {
            // TODO: 这里需要你根据你的应用程序架构来处理新创建的编辑器
            // 比如将编辑器添加到主窗口的某个面板中
        }

        public override void OnActivated()
        {
            base.OnActivated();
        }
    }
}