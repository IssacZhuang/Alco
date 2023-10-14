using System;
using System.Linq;
using System.Text;
using System.Reflection;

namespace Vocore.Engine
{
    public static class EmbbedResources
    {
        private static Assembly? _assembly;
        public static Assembly Assembly
        {
            get
            {
                if (_assembly == null)
                {
                    _assembly = Assembly.GetExecutingAssembly();
                }
                return _assembly;
            }
        }

        public static readonly string Prefix = "Vocore.Engine.Assets.";

        public static string[] AllFileNames
        {
            get
            {
                return Assembly.GetManifestResourceNames();
            }
        }

        public static byte[] GetBytes(string path)
        {
            //get embbed asset in dll
            var stream = Assembly.GetManifestResourceStream(path);
            if (stream == null)
            {
                Log.Error($"Embbed Resource {path} not found");
                return new byte[0];
            }
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            return bytes;
        }

        public static string GetText(string path)
        {
            return Encoding.UTF8.GetString(GetBytes(path));
        }

        public static IEnumerable<string> GetAllFileNamesWithExtension(string extension)
        {
            return AllFileNames.Where(x => x.EndsWith(extension));
        }
    }
}

