using System;
using System.Collections.Generic;

namespace Vocore
{
    public interface IVirtualDirectory
    {
        bool TryGetData(string path, out byte[] data);
    }
}

