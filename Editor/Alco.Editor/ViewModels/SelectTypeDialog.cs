

using System;
using System.Collections.Generic;
using System.Linq;

namespace Alco.Editor.ViewModels;

public class SelectTypeDialog : ViewModelBase
{
    public List<TypeItem> Types { get; } = new List<TypeItem>();
    public TypeItem? SelectedType { get; set; }

    public event Action<Type>? OnTypeConfirmed;


    public SelectTypeDialog(params Type[] types)
    {
        Types.AddRange(types.Select(t => new TypeItem(t)));
    }

    public Views.SelectTypeDialog CreateControl()
    {
        return new Views.SelectTypeDialog
        {
            DataContext = this
        };
    }

    public void Confirm()
    {
        if (SelectedType == null)
        {
            return;
        }

        OnTypeConfirmed?.Invoke(SelectedType.Type);
    }
}
