namespace Alco.GUI;

/// <summary>
/// Interface for paged list controls that support page navigation.
/// </summary>
public interface IUIPageList
{
    /// <summary>
    /// Gets the current page index (0-based).
    /// </summary>
    int CurrentPage { get; }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    int TotalPages { get; }

    /// <summary>
    /// Gets whether there is a previous page available.
    /// </summary>
    bool HasPreviousPage { get; }

    /// <summary>
    /// Gets whether there is a next page available.
    /// </summary>
    bool HasNextPage { get; }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    /// <returns>True if navigation succeeded, false if already at first page.</returns>
    bool PreviousPage();

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    /// <returns>True if navigation succeeded, false if already at last page.</returns>
    bool NextPage();
}
