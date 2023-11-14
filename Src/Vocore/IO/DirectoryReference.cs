using System;
using System.Collections.Generic;

using System.IO;

namespace Vocore
{
    public class DirectoryReference
    {
        private readonly DirectoryInfo _directoryInfo;
        private readonly HashSet<FileReference> _files = new HashSet<FileReference>();

        public IEnumerable<FileReference> LoadedFiles
        {
            get
            {
                return _files;
            }
        }

        public DirectoryReference(string path)
        {
            _directoryInfo = new DirectoryInfo(path);
        }

        public bool Validate()
        {
            return _directoryInfo.Exists;
        }

        public void LoadFiles()
        {
            if (!Validate())
            {
                return;
            }
            _files.Clear();
            foreach (var file in _directoryInfo.GetFiles())
            {
                _files.Add(new FileReference(file.FullName));
            }
        }

        public void LoadFiles(string extension)
        {
            if (!Validate())
            {
                return;
            }
            _files.Clear();
            foreach (var file in _directoryInfo.GetFiles(extension))
            {
                _files.Add(new FileReference(file.FullName));
            }
        }

        public void LoadFiles(params string[] extensions)
        {
            if (!Validate())
            {
                return;
            }
            _files.Clear();
            foreach (var file in _directoryInfo.GetFiles())
            {
                foreach (var extension in extensions)
                {
                    if (file.Extension == extension)
                    {
                        _files.Add(new FileReference(file.FullName));
                    }
                }
            }
        }

    }
}

