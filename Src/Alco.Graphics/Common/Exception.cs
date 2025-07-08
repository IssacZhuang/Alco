namespace Alco.Graphics;

public class GraphicsException : Exception
{
    public GraphicsException(string message) : base(message)
    {
    }

    public static void ThrowIfDisposed(BaseGPUObject obj)
    {
        if (obj.IsDisposed)
        {
            throw new GraphicsException($"Object {obj.Name} is disposed.");
        }
    }
}

