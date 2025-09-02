namespace Alco.GUI;

public abstract class UIList<TData> : UINode
{
    //used for virtual list
    private readonly List<UINode> _pool = new();
    private readonly List<TData> _data = new();

    protected abstract UINode CreateItem();

    protected static void SetDataForItem(UINode item, int index, TData data)
    {
        if (item is IUIListItem<TData> uiListItem)
        {
            uiListItem.SetData(index, data);
        }
    }
}