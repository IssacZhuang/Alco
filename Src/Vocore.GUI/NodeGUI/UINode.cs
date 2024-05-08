using System.Runtime.CompilerServices;

namespace Vocore.GUI;

public abstract class UINode
{
    private string _name = string.Empty;

    private bool _isDirty = false;
    private bool _isVisible = true;

    public Transform2D transform = Transform2D.Identity;
    public Pivot pivot = Pivot.Center;
    public Anchor anchor = Anchor.Center;


    public string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _name;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _name = value;
    }

    public bool IsDirty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isDirty;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _isDirty = value;
    }

    public bool IsVisible
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isVisible;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _isVisible = value;
    }
}