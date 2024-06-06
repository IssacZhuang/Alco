using System;

namespace Vocore.IO
{
    /// <summary>
    /// The cache mode of the asset
    /// </summary>
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
        /// The asset will be cached in memory and never collect by GC
        /// </summary>
        Persistent
    }
}