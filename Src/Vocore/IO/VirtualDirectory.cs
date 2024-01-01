using System;
using System.Collections.Generic;

namespace Vocore
{
    public class VirtualDirectory : IFileSource
    {
        private readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();

        public int FileCount => _data.Count;
        public int Order => 1;

        public IEnumerable<string> AllFileNames
        {
            get
            {
                return _data.Keys;
            }
        }

        public IEnumerable<KeyValuePair<string, byte[]>> AllFiles
        {
            get
            {
                return _data;
            }
        }

        

        public bool TryAddData(string path, byte[] data)
        {
            if (data == null)
            {
                return false;
            }
            if (_data.ContainsKey(path))
            {
                return false;
            }
            _data.Add(path, data);
            return true;
        }

        //add or update
        public bool TrySetData(string path, byte[] data)
        {
            if (data == null)
            {
                return false;
            }
            _data[path] = data;
            return true;
        }

        public bool TryGetData(string path, out byte[] data)
        {
            return _data.TryGetValue(path, out data);
        }
    }
}

