using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;

namespace Vocore
{
    public class ResourcePack : IDisposable
    {
        private readonly ZipArchive _archive;
        public ZipArchive Archive => _archive;

        public ResourcePack(string path)
        {
            if (File.Exists(path))
            {
                _archive = ZipFile.Open(path, ZipArchiveMode.Update);
            }
            else
            {
                ZipFile.Open(path, ZipArchiveMode.Create).Dispose();
                _archive = ZipFile.Open(path, ZipArchiveMode.Update);
            }
        }

        public bool TrySetFile(string path, byte[] file)
        {
            if (file == null)
            {
                return false;
            }

            _archive.GetEntry(path)?.Delete();

            ZipArchiveEntry entry = _archive.CreateEntry(path);

            using (Stream stream = entry.Open())
            {
                stream.Write(file, 0, file.Length);
            }
            return true;
        }

        public bool TrySetTextFile(string path, string text)
        {
            if (text == null)
            {
                return false;
            }

            _archive.GetEntry(path)?.Delete();

            ZipArchiveEntry entry = _archive.CreateEntry(path);

            using (StreamWriter writer = new StreamWriter(entry.Open()))
            {
                writer.Write(text);
            }
            return true;
        }

        public bool IsFileExist(string path)
        {
            return _archive.GetEntry(path) != null;
        }

        public Stream GetStream(string path)
        {
            if (_archive.GetEntry(path) == null)
            {
                return null;
            }

            return _archive.GetEntry(path).Open();
        }

        public bool TryGetFileBinary(string path, out byte[] binary)
        {
            if (_archive.GetEntry(path) == null)
            {
                binary = null;
                return false;
            }

            using (MemoryStream stream = new MemoryStream())
            {
                _archive.GetEntry(path).Open().CopyTo(stream);
                binary = stream.ToArray();
            }
            return true;
        }

        public bool TryGetFileText(string path, out string text)
        {
            if (_archive.GetEntry(path) == null)
            {
                text = null;
                return false;
            }

            using (StreamReader reader = new StreamReader(_archive.GetEntry(path).Open()))
            {
                text = reader.ReadToEnd();
            }
            return true;
        }

        public bool TryDeleteFile(string path)
        {
            if (_archive.GetEntry(path) == null)
            {
                return false;
            }

            _archive.GetEntry(path).Delete();
            return true;
        }

        public void Dispose()
        {
            _archive.Dispose();
        }
    }
}