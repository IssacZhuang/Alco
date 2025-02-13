using System;

namespace Alco.Editor.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
