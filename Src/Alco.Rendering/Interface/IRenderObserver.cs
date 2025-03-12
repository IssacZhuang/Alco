using Alco.Graphics;

namespace Alco.Rendering;

public interface ICommandObserver
{
    public void OnCommandBegin();
    public void OnCommandEnd();
}