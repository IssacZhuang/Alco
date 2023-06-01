using System;
using System.Reflection;

namespace Terminal
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RegisterCommandAttribute : Attribute
    {
        private int _minArgCount = 0;
        private int _maxArgCount = -1;

        public int MinArgCount {
            get { return _minArgCount; }
            set { _minArgCount = value; }
        }

        public int MaxArgCount {
            get { return _maxArgCount; }
            set { _maxArgCount = value; }
        }

        public string Name { get; set; }
        public string Help { get; set; }
        public string Hint { get; set; }

        public RegisterCommandAttribute(string commandName = null) {
            Name = commandName;
        }
    }
}
