using System;

namespace Alco.Editor.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MenuItemAttribute : Attribute
    {
        public string Path { get; }
        public int Order { get; }
        public MenuItemAttribute(string path, int order = 0)
        {
            Path = path;
            Order = order;
        }
    }
}
