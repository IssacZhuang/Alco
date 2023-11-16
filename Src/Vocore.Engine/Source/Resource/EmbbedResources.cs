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
                    _assembly = typeof(EmbbedResources).Assembly;
                }
                return _assembly;
            }
        }

        public static readonly string PathShaderLib = "Assets/Shader/Lib/";
        public static readonly string PathGraphicsShader = "Assets/Shader/Graphics/";
        public static readonly string PathComputeShader = "Assets/Shader/Compute/";

        public static string[] AllFileNames
        {
            get
            {
                return Assembly.GetManifestResourceNames();
            }
        }

        public static bool IsShaderLib(string path, out string filename)
        {
            filename = path.Replace(PathShaderLib, "");
            return path.StartsWith(PathShaderLib);
        }

        public static bool IsGraphicsShader(string path, out string filename)
        {
            filename = path.Replace(PathGraphicsShader, "");
            return path.StartsWith(PathGraphicsShader);
        }

        public static bool IsComputeShader(string path, out string filename)
        {
            filename = path.Replace(PathComputeShader, "");
            return path.StartsWith(PathComputeShader);
        }   

        public static byte[] GetBytes(string path)
        {
            //get embbed asset in dll
            var stream = Assembly.GetManifestResourceStream(path);
            if (stream == null)
            {
                Log.Error($"Embbed Resource {path} not found");
                return Array.Empty<byte>();
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

