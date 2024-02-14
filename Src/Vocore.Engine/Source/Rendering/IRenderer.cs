using Vocore.Graphics;

namespace Vocore.Engine;

public interface IRenderer
{
    GPUCommandBuffer Buffer { get; }
}