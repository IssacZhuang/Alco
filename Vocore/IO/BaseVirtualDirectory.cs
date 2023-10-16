using System;
using System.Collections.Generic;

namespace Vocore
{
    public class BaseVirtualDirectory : IVirtualDirectory
    {
        private readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();

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
            return true;
        }

        //add or update
        public bool TrySetSetData(string path, byte[] data)
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

