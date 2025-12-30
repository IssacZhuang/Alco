using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MsdfFontGenerator.ViewModels;

public partial class LanguageSelectionItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;
    
    public string Name { get; }
    public int Value { get; }
    public string Description { get; }
    
    public event Action? SelectionChanged;

    public LanguageSelectionItem(string name, int value, string description)
    {
        Name = name;
        Value = value;
        Description = description;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }
}