using System;

namespace Vocore.Engine
{
    public enum AssetCacheMode
    {
        /// <summary>
        /// The asset will not be cached
        /// </summary>
        None = 0,
        /// <summary>
        /// The asset will be cached in memory and will collect by GC when it is not used
        /// </summary>
        Recyclable,
        /// <summary>
        /// The asset will be cached in memory and will not collect by GC
        /// </summary>
        Persistent
    }
}