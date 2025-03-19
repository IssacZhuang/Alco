using System;
using System.Collections.Generic;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Alco.Editor.ViewModels;

public class TypeItem : ObservableObject
{
    public Type Type { get; }

    public string Name => Type.Name;

    public TypeItem(Type type)
    {
        Type = type;
    }

}