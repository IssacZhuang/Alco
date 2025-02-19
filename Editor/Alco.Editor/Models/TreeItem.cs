using System;
using System.Collections.Generic;
using System.Linq;

namespace Alco.Editor.Models;

public class TreeItem<TUserData>
{
    public string Header { get; set; } = string.Empty;
    public TUserData? UserData { get; set; }
    public Dictionary<string, TreeItem<TUserData>> Child { get; set; } = new();
}

public static class TreeItemExtension
{
    public static void AddTreeItem<TUserData>(this IList<TreeItem<TUserData>> tree, string path, TUserData userData)
    {
        var pathParts = path.Split('/');
        var currentLevel = tree;
        TreeItem<TUserData>? currentItem = null;

        for (int i = 0; i < pathParts.Length; i++)
        {
            var segment = pathParts[i];

            if (i == 0) // Top level item
            {
                currentItem = currentLevel.FirstOrDefault(x => x.Header == segment);
                if (currentItem == null)
                {
                    currentItem = new TreeItem<TUserData> { Header = segment };
                    currentLevel.Add(currentItem);
                }
            }
            else // Sub item
            {
                if (!currentItem!.Child.TryGetValue(segment, out var childItem))
                {
                    childItem = new TreeItem<TUserData> { Header = segment };
                    currentItem.Child[segment] = childItem;
                }
                currentItem = childItem;
            }

            // Set user data for the last segment
            if (i == pathParts.Length - 1)
            {
                currentItem.UserData = userData;
            }
        }
    }
}