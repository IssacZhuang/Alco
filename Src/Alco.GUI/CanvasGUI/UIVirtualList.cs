namespace Alco.GUI;

public abstract class UIVirtualList<TData> : UINode
{
    protected abstract UINode CreateItem();
}