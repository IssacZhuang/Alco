namespace Alco.GUI;

public abstract class UIVirtualList<TData> : UINode
{
    private struct ActiveItem
    {
        public UINode Node;
        public int Index;

        public ActiveItem(UINode node, int index)
        {
            Node = node;
            Index = index;
        }
    }

    private readonly Deque<ActiveItem> _activeItems = new();

    protected abstract UINode CreateItem();

    protected int GetStartIndex()
    {
        //todo
        return 0;
    }

    protected int GetEndIndex()
    {
        //todo
        return 0;
    }

    protected void SetDataForItem(UINode item, int index, TData data)
    {
        if (item is IUIListItem<TData> uiListItem)
        {
            uiListItem.SetData(index, data);
        }
    }

    private class VirtualContainer: UINode{
        //todo
    }
}