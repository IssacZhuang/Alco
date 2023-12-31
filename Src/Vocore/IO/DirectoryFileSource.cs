using System;
using System.IO;
using System.Collections.Generic;

namespace Vocore
{
    public class DirectoryFileSource : IFileSource
    {
        private string _directoryPath;

        public DirectoryFileSource(string directoryPath)
        {
            _directoryPath = directoryPath;
        }

        public IEnumerable<string> AllFileNames
        {
            get
            {
                return Directory.EnumerateFiles(_directoryPath);
            }
        }

        public bool TryGetData(string path, out byte[] data)
        {
            try
            {
                data = File.ReadAllBytes(Path.Combine(_directoryPath, path));
                return true;
            }
            catch (Exception)
            {
                data = null;
                return false;
            }
        }
    }
}