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
        public UserControl Content => _content ??= CreateContent();
        protected abstract UserControl CreateContent();
        public virtual void OnActivated() { }
        public virtual void OnDeactivated() { }
    }
}