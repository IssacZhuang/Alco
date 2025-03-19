using Alco.Graphics;

namespace Alco.Rendering;

public interface ICommandListener
{
    public void OnCommandBegin();
    public void OnCommandEnd();
}