namespace Alco.Graphics
{
    public enum AccessMode
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        ReadWrite = Read | Write,
    }
}