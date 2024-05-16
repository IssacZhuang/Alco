using System.Numerics;

namespace Vocore.GUI;

public interface IScrollable
{
    void OnScroll(Vector2 displacement);
}