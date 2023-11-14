using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Vocore
{
    public class FileReference : IEquatable<FileReference>
    {
        private readonly FileInfo _fileInfo;

        public string Path
        {
            get
            {
                if (_fileInfo == null) return "";
                return _fileInfo.FullName;
            }
        }

        public FileReference(string path)
        {
            _fileInfo = new FileInfo(path);
        }

        public bool Validate()
        {
            return _fileInfo.Exists;
        }

        public byte[] GetBytes()
        {
            if (!Validate()) throw new FileNotFoundException(_fileInfo.FullName);
            return File.ReadAllBytes(Path);
        }

        public string GetString()
        {
            if (!Validate()) throw new FileNotFoundException(_fileInfo.FullName);
            return File.ReadAllText(Path);
        }

        public Stream GetStream()
        {
            if (!Validate()) throw new FileNotFoundException(_fileInfo.FullName);
            return _fileInfo.OpenRead();
        }

        public void LoadBytesAsync(Action<byte[]> callback)
        {
            ThreadPool.QueueUserWorkItem((state) =>
            {
                callback(GetBytes());
            });
        }

        public Task LoadStringAsync(Action<string> success, Action<Exception> fail = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    success(GetString());
                }
                catch (Exception e)
                {
                    if (fail != null) fail(e);
                }
            });
        }

        public Task LoadStreamAsync(Action<Stream> success, Action<Exception> fail = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    success(GetStream());
                }
                catch (Exception e)
                {
                    if (fail != null) fail(e);
                }
            });
        }

        public bool Equals(FileReference other)
        {
            return other != null && other.Path == Path;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FileReference);
        }

        //override operator ==
        public static bool operator ==(FileReference a, FileReference b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (ReferenceEquals(a, null)) return false;
            if (ReferenceEquals(b, null)) return false;
            return a.Equals(b);
        }

        public static bool operator !=(FileReference a, FileReference b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return Path.GetHashCode();
        }
    }
}
