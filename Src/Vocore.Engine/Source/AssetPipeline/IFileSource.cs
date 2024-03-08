using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vocore
{
    public interface IFileSource
    {
        /// <summary>
        /// The order of this file source, the higher order will be override the lower order
        /// </summary>
        int Order { get; }
        /// <summary>
        /// All file names in this file source
        /// </summary>
        IEnumerable<string> AllFileNames { get; }
        /// <summary>
        /// Try get data from this file source
        /// </summary>
        bool TryGetData(string path, [NotNullWhen(true)] out byte[]? data);
    }
}

