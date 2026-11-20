// Example-level material instance: live parent bindings and coalesced local root ranges copied into caller-owned storage.

using System.Diagnostics;

namespace Alco.Graphics;

/// <summary>A material with its parent's initial render state and live inheritance of compatible root fields and specialization unless overridden.</summary>
/// <remarks>Concurrent reads require the instance and all of its ancestors to remain unchanged.</remarks>
public sealed class MaterialInstance : Material
{
    private readonly Material _parent;
    private (uint Offset, uint SizeBytes)[] _overrides = [];
    private int _overrideCount;
    private bool _hasLocalSpecializations;

    internal MaterialInstance(Material parent) : base(parent.Shader, parent.Name + "_instance", parent.Variant)
    {
        _parent = parent;
        if (Shader.Kind != ShaderKind.Compute)
        {
            Blend = parent.Blend;
            WriteMask = parent.WriteMask;
            Rasterization = parent.Rasterization;
            Topology = parent.Topology;
            DepthStencilState = parent.DepthStencilState;
        }
    }

    /// <summary>The material this instance resolves unset root fields from.</summary>
    public Material Parent => _parent;

    internal override ShaderVariant Variant => _hasLocalSpecializations ? base.Variant : _parent.Variant;

    private protected override bool PinSpecializations()
    {
        if (_hasLocalSpecializations)
            return false;
        _hasLocalSpecializations = true;
        return true;
    }

    private protected override void RemapOverrides(RootLayout oldRoot, RootLayout newRoot)
    {
        if (_overrideCount == 0)
            return;
        ReadOnlySpan<(uint Offset, uint SizeBytes)> oldOverrides = _overrides.AsSpan(0, _overrideCount);
        _overrides = [];
        _overrideCount = 0;
        foreach (RootField newField in newRoot.LeafFields)
        {
            RootField? oldField = oldRoot.FindCompatibleLeaf(newField);
            if (oldField != null && IsOverridden(oldField, oldOverrides))
                OnFieldSet(newField, true);
        }
    }

    private static bool IsOverridden(RootField field, ReadOnlySpan<(uint Offset, uint SizeBytes)> overrides)
    {
        foreach ((uint offset, uint sizeBytes) in overrides)
        {
            if (offset > field.Offset)
                break;
            if (field.Offset + field.SizeBytes <= offset + sizeBytes)
                return true;
        }
        return false;
    }

    private protected override void OnFieldSet(RootField field, bool overridden)
    {
        uint start = field.Offset;
        uint end = start + field.SizeBytes;
        if (!overridden)
        {
            for (int index = 0; index < _overrideCount; ++index)
            {
                (uint offset, uint sizeBytes) = _overrides[index];
                uint rangeEnd = offset + sizeBytes;
                if (start >= rangeEnd)
                    continue;
                if (end <= offset)
                    return;
                Debug.Assert(start >= offset && end <= rangeEnd);
                if (start == offset && end == rangeEnd)
                {
                    --_overrideCount;
                    Array.Copy(_overrides, index + 1, _overrides, index, _overrideCount - index);
                }
                else if (start == offset)
                    _overrides[index] = (end, rangeEnd - end);
                else if (end == rangeEnd)
                    _overrides[index] = (offset, start - offset);
                else
                {
                    if (_overrideCount == _overrides.Length)
                        Array.Resize(ref _overrides, Math.Max(4, _overrides.Length * 2));
                    Array.Copy(_overrides, index + 1, _overrides, index + 2, _overrideCount - index - 1);
                    _overrides[index] = (offset, start - offset);
                    _overrides[index + 1] = (end, rangeEnd - end);
                    ++_overrideCount;
                }
                return;
            }
            return;
        }

        int first = 0;
        while (first < _overrideCount && _overrides[first].Offset + _overrides[first].SizeBytes < start)
            ++first;
        if (first == _overrideCount || _overrides[first].Offset > end)
        {
            if (_overrideCount == _overrides.Length)
                Array.Resize(ref _overrides, Math.Max(4, _overrides.Length * 2));
            Array.Copy(_overrides, first, _overrides, first + 1, _overrideCount - first);
            _overrides[first] = (start, field.SizeBytes);
            ++_overrideCount;
            return;
        }
        if (_overrides[first].Offset <= start && _overrides[first].Offset + _overrides[first].SizeBytes >= end)
            return;
        start = Math.Min(start, _overrides[first].Offset);
        int last = first;
        while (last < _overrideCount && _overrides[last].Offset <= end)
        {
            end = Math.Max(end, _overrides[last].Offset + _overrides[last].SizeBytes);
            ++last;
        }
        Array.Copy(_overrides, last, _overrides, first + 1, _overrideCount - last);
        _overrideCount -= last - first - 1;
        _overrides[first] = (start, end - start);
    }

    internal override void CopyRoot(Span<byte> destination) => CopyRoot(destination, 0);

    internal override void CopyRoot(Span<byte> destination, int offset)
    {
        if (_hasLocalSpecializations || _overrideCount != 0)
        {
            RootLayout localRoot = StagingRoot;
            RootLayout parentRoot = _parent.Root;
            if (!localRoot.MatchesLayout(parentRoot))
            {
                CopyRemappedRoot(destination, offset, localRoot, parentRoot);
                return;
            }
        }

        int end = offset + destination.Length;
        int cursor = offset;
        for (int index = 0; index < _overrideCount; ++index)
        {
            (uint rangeOffset, uint sizeBytes) = _overrides[index];
            int rangeEnd = (int)(rangeOffset + sizeBytes);
            if (rangeEnd <= cursor)
                continue;
            if ((int)rangeOffset >= end)
                break;
            int start = Math.Max(cursor, (int)rangeOffset);
            if (cursor < start)
                _parent.CopyRoot(destination.Slice(cursor - offset, start - cursor), cursor);
            int stop = Math.Min(end, rangeEnd);
            _staging.AsSpan(start, stop - start).CopyTo(destination.Slice(start - offset, stop - start));
            cursor = stop;
        }
        if (cursor < end)
            _parent.CopyRoot(destination.Slice(cursor - offset), cursor);
    }

    private void CopyRemappedRoot(Span<byte> destination, int offset, RootLayout localRoot, RootLayout parentRoot)
    {
        RootLayout root;
        if (_hasLocalSpecializations)
        {
            root = localRoot;
            _staging.AsSpan(offset, destination.Length).CopyTo(destination);
        }
        else
        {
            root = parentRoot;
            _parent.CopyRoot(destination, offset);
        }

        int end = offset + destination.Length;
        ReadOnlySpan<(uint Offset, uint SizeBytes)> overrides = _overrides.AsSpan(0, _overrideCount);
        foreach (RootField field in root.LeafFields)
        {
            int fieldStart = (int)field.Offset;
            int start = Math.Max(offset, fieldStart);
            int size = Math.Min(end, fieldStart + (int)field.SizeBytes) - start;
            if (size <= 0)
                continue;
            Span<byte> slice = destination.Slice(start - offset, size);
            int fieldOffset = start - fieldStart;
            if (_hasLocalSpecializations)
            {
                RootField? inherited = parentRoot.FindCompatibleLeaf(field);
                if (inherited != null && !IsOverridden(field, overrides))
                    _parent.CopyRoot(slice, (int)inherited.Offset + fieldOffset);
            }
            else
            {
                RootField? local = localRoot.FindCompatibleLeaf(field);
                if (local != null && IsOverridden(local, overrides))
                    _staging.AsSpan((int)local.Offset + fieldOffset, size).CopyTo(slice);
            }
        }
    }

    internal override Material ResourceOwner =>
        _bindingCount == 0 && (!_hasLocalSpecializations || StagingRoot.MatchesLayout(_parent.Root)) ? _parent.ResourceOwner : this;

    internal override uint ResourceVersion => base.ResourceVersion + _parent.ResourceVersion;

    private protected override void RetainBindings(List<RenderingResource> resources)
    {
        bool retainWhiteTexture = false;
        foreach (RootField field in Variant.Root.LeafFields)
        {
            if (field.Kind == MaterialFieldKind.Data)
                continue;
            RenderingResource? resource = GetBinding(field);
            if (resource != null)
            {
                Debug.Assert(!resource.IsDestroyed);
                resources.Add(resource);
            }
            else if (field.Kind == MaterialFieldKind.Texture)
                retainWhiteTexture = true;
        }
        if (retainWhiteTexture)
            resources.Add(Shader.System.TextureWhite);
    }

    internal override RenderingResource? GetBinding(RootField field)
    {
        RootLayout parentRoot = _parent.Root;
        bool sameLayout = ReferenceEquals(StagingRoot, parentRoot);
        if (_bindingCount != 0)
        {
            RootField? localField = _hasLocalSpecializations || sameLayout ? field : StagingRoot.FindCompatibleLeaf(field);
            if (localField != null && GetStagedBinding(localField.Offset) is RenderingResource binding)
                return binding;
        }
        RootField? parentField = !_hasLocalSpecializations || sameLayout ? field : parentRoot.FindCompatibleLeaf(field);
        return parentField == null ? null : _parent.GetBinding(parentField);
    }
}
