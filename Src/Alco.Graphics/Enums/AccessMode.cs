namespace Alco.Graphics
{
    /// <summary>
    /// The kind of access requested for a mapped or bound resource.
    /// </summary>
    public enum AccessMode
    {
        /// <summary>No access.</summary>
        None = 0,
        /// <summary>Read-only access.</summary>
        Read = 1 << 0,
        /// <summary>Write-only access.</summary>
        Write = 1 << 1,
        /// <summary>Both read and write access.</summary>
        ReadWrite = Read | Write,
    }
}