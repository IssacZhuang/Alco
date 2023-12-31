using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Vocore
{
    public class ResourcePack : IDisposable, IFileSource
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

        public IEnumerable<string> AllFileNames
        {
            get
            {
                foreach (ZipArchiveEntry entry in _archive.Entries)
                {
                    yield return entry.FullName;
                }
            }
        }

        public bool TrySetData(string path, byte[] file)
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

        public unsafe bool TrySetFileByPointer(string path, void* ptr, int length)
        {
            if (ptr == null || length <= 0)
            {
                return false;
            }

            _archive.GetEntry(path)?.Delete();

            ZipArchiveEntry entry = _archive.CreateEntry(path);

            using (Stream stream = entry.Open())
            {
                byte* p = (byte*)ptr;
                for (int i = 0; i < length; i++)
                {
                    stream.WriteByte(p[i]);
                }
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

        public bool TryGetData(string path, out byte[] binary)
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

        public bool TryDeleteFile(string path)
        {
            ZipArchiveEntry entry = _archive.GetEntry(path);
            if ( entry == null)
            {
                return false;
            }

            entry.Delete();
            return true;
        }

        public void Dispose()
        {
            _archive.Dispose();
        }
    }
}