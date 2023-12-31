using System;
using System.Collections.Generic;

namespace Vocore
{
    public interface IFileSource
    {
        IEnumerable<string> AllFileNames { get; }
        bool TryGetData(string path, out byte[] data);
    }
}

