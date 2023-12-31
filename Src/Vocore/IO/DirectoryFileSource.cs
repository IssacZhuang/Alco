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
                //list all files in directory or sub directory with relative path
                foreach (var file in Directory.EnumerateFiles(_directoryPath, "*", SearchOption.AllDirectories))
                {
                    yield return Path.GetRelativePath(_directoryPath, file);
                }
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