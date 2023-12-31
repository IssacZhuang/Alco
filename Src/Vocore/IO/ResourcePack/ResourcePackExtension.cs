using System;
using System.IO;
using System.Text;

namespace Vocore
{
    public static class ResourcePackExtension
    {
        public static bool TrySetTextFile(this ResourcePack pack, string path, string text)
        {
            if (text == null)
            {
                return false;
            }

            if (pack.TrySetData(path, Encoding.UTF8.GetBytes(text)))
            {
                return true;
            }
            return false;
        }

        public static bool TryGetFileText(this ResourcePack pack, string path, out string text)
        {
            if (pack.TryGetData(path, out byte[] data))
            {
                text = Encoding.UTF8.GetString(data);
                return true;
            }

            text = null;
            return false;
        }
    }
}