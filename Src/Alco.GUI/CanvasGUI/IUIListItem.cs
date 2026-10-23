namespace Alco.GUI;

/// <summary>
/// A UI list item that can be bound to a data item by index.
/// </summary>
/// <typeparam name="TData">The type of data bound to the item.</typeparam>
public interface IUIListItem<TData>
{
    /// <summary>
    /// Binds the given data item to this list item.
    /// </summary>
    /// <param name="index">The item's index in the list.</param>
    /// <param name="data">The data item to display.</param>
    void SetData(int index, TData data);
}