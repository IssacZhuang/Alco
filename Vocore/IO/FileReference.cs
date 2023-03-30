using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace Vocore
{
    public class FileReference
    {
        private FileInfo _fileInfo;

        public string Path
        {
            get { return _fileInfo.FullName; }
        }

        public FileReference(string path)
        {
            Link(path);
        }

        public void Link(string path)
        {
            _fileInfo = new FileInfo(path);
        }

        public bool Validate()
        {
            return _fileInfo.Exists;
        }

        public byte[] GetBytes()
        {
            if (!Validate()) throw ExceptionIO.ExceptionFilePathInvalid;
            return File.ReadAllBytes(_fileInfo.FullName);
        }

        public string GetString()
        {
            if (!Validate()) throw ExceptionIO.ExceptionFilePathInvalid;
            return File.ReadAllText(Path);
        }

        public Stream GetStream()
        {
            if (!Validate()) throw ExceptionIO.ExceptionFilePathInvalid;
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
    }
}
