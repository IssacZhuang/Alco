namespace Alco.Audio;

/// <summary>
/// Represents an audio bus for hierarchical volume control.
/// Buses can be nested to create a tree structure (e.g., Master -> SFX -> Explosion).
/// </summary>
public sealed class AudioBus
{
    private float _localVolume = 1f;
    private float _effectiveVolume = 1f;
    private readonly List<AudioBus> _children = new();
    private AudioBus? _parent;

    private readonly WeakEvent _onVolumeChanged = new();

    /// <summary>
    /// Gets the name of the bus for identification.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Occurs when the effective volume of this bus changes.
    /// </summary>
    public event Action OnVolumeChanged
    {
        add => _onVolumeChanged.AddListener(value);
        remove => _onVolumeChanged.RemoveListener(value);
    }

    /// <summary>
    /// Gets or sets the local volume of this bus (0.0 to 1.0).
    /// </summary>
    public float Volume
    {
        get => _localVolume;
        set
        {
            value = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_localVolume - value) < float.Epsilon) return;
            _localVolume = value;
            UpdateEffectiveVolume();
        }
    }

    /// <summary>
    /// Gets the effective volume, which is the product of this bus's local volume 
    /// and all its ancestors' local volumes.
    /// </summary>
    public float EffectiveVolume => _effectiveVolume;

    /// <summary>
    /// Gets or sets the parent bus.
    /// </summary>
    public AudioBus? Parent
    {
        get => _parent;
        set
        {
            if (_parent == value) return;

            if (_parent != null)
            {
                _parent._children.Remove(this);
            }

            _parent = value;

            if (_parent != null)
            {
                _parent._children.Add(this);
            }

            UpdateEffectiveVolume();
        }
    }

    public AudioBus(string name)
    {
        Name = name;
    }

    private void UpdateEffectiveVolume()
    {
        float newEffectiveVolume = _localVolume * (_parent?.EffectiveVolume ?? 1f);
        if (Math.Abs(_effectiveVolume - newEffectiveVolume) < float.Epsilon) return;

        _effectiveVolume = newEffectiveVolume;
        _onVolumeChanged.Invoke();

        // Directly notify children instead of using events
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].UpdateEffectiveVolume();
        }
    }
}
