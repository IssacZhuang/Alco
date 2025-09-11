namespace Alco.GUI;

public interface IUIListItem<TData>
{
    void SetData(int index, TData data);
}