using Avalonia.Controls;
using Avalonia.Media;

namespace Alco.Editor.Models
{
    public abstract class Page
    {
        private StreamGeometry? _iconGeometry;
        private UserControl? _content;

        public abstract string IconData { get; }
        public abstract string Tooltip { get; }

        public StreamGeometry IconGeometry => _iconGeometry ??= StreamGeometry.Parse(IconData);

        // 页面内容
        public UserControl Content => _content ??= CreateContent();

        // 创建页面内容
        protected abstract UserControl CreateContent();

        // 激活页面时调用
        public virtual void OnActivated() { }

        // 停用页面时调用
        public virtual void OnDeactivated() { }
    }
}