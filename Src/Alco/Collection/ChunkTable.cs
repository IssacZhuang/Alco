using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco
{
    /// <summary>
    /// Single-writer, multi-reader (SWMR) sparse chunk directory keyed by signed <see cref="int2"/> coordinates.
    /// Provides bounded worst-case lookups via an 8-slot linear probe window backed by a 16-way spatial radix tree.
    /// </summary>
    /// <typeparam name="TChunk">The reference chunk type stored in the table.</typeparam>
    public sealed class ChunkTable<TChunk> where TChunk : class
    {
        private const int MinCapacity = 64;
        private const int MaxProbe = 8;
        private const int RadixLevels = 16;

        private sealed class Entry(int2 key, TChunk value)
        {
            public readonly int2 Key = key;
            public readonly TChunk Value = value;
        }

        private sealed class RadixBranch
        {
            public readonly object?[] Children = new object?[16];
        }

        private sealed class TableState
        {
            public readonly Entry?[] Slots;
            public readonly int Mask;
            public object? OverflowRoot;

            public TableState(int capacity)
            {
                Slots = new Entry?[capacity];
                Mask = capacity - 1;
            }
        }

        private static readonly Entry s_tombstone = new(default, null!);

        private TableState _state;
        private int _count;
        private int _tombstoneCount;
        private int _overflowCount;

        /// <summary>Initializes an empty <see cref="ChunkTable{TChunk}"/>.</summary>
        /// <param name="initialCapacity">Minimum initial slot capacity; rounded up to a power of two at least 64.</param>
        public ChunkTable(int initialCapacity = MinCapacity)
        {
            int capacity = RoundUpCapacity(initialCapacity);
            _state = new TableState(capacity);
        }

        /// <summary>Gets the number of chunks currently stored in the table.</summary>
        public int Count => Volatile.Read(ref _count);

        internal int Capacity => Volatile.Read(ref _state).Slots.Length;

        internal int OverflowCount => _overflowCount;

        /// <summary>Creates a stack-only read cursor caching the most recently accessed chunk coordinate.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkReadCursor<TChunk> CreateReadCursor() => new(this);

        /// <summary>Determines whether the table contains a chunk at <paramref name="coord"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int2 coord) => TryGet(coord, out _);

        /// <summary>
        /// Attempts to retrieve the chunk at <paramref name="coord"/>.
        /// Wait-free and allocation-free; safe to call concurrently with single-writer mutations.
        /// </summary>
        /// <param name="coord">The chunk coordinate.</param>
        /// <param name="chunk">The chunk if present; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if found; otherwise <see langword="false"/>.</returns>
        public bool TryGet(int2 coord, [NotNullWhen(true)] out TChunk? chunk)
        {
            TableState state = Volatile.Read(ref _state);
            Entry?[] slots = state.Slots;
            int mask = state.Mask;
            int start = (int)HashCoord(coord) & mask;

            for (int i = 0; i < MaxProbe; i++)
            {
                Entry? entry = Volatile.Read(ref slots[(start + i) & mask]);
                if (entry == null)
                {
                    chunk = null;
                    return false;
                }

                if (!ReferenceEquals(entry, s_tombstone) && entry.Key == coord)
                {
                    chunk = entry.Value;
                    return true;
                }
            }

            object? root = Volatile.Read(ref state.OverflowRoot);
            if (root != null && TryGetOverflow(root, coord, out chunk))
            {
                return true;
            }

            chunk = null;
            return false;
        }

        /// <summary>Attempts to insert <paramref name="chunk"/> at <paramref name="coord"/> if not already present.</summary>
        /// <param name="coord">The chunk coordinate.</param>
        /// <param name="chunk">The chunk instance to store.</param>
        /// <returns><see langword="true"/> if inserted; <see langword="false"/> if the key already exists.</returns>
        public bool TryAdd(int2 coord, TChunk chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);

            TableState state = _state;
            Entry?[] slots = state.Slots;
            int mask = state.Mask;
            int start = (int)HashCoord(coord) & mask;
            int firstTombstone = -1;
            int emptySlot = -1;

            for (int i = 0; i < MaxProbe; i++)
            {
                int slotIdx = (start + i) & mask;
                Entry? entry = slots[slotIdx];
                if (entry == null)
                {
                    emptySlot = slotIdx;
                    break;
                }

                if (ReferenceEquals(entry, s_tombstone))
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = slotIdx;
                    }
                    continue;
                }

                if (entry.Key == coord)
                {
                    return false;
                }
            }

            if (emptySlot < 0 && state.OverflowRoot != null && ContainsOverflow(state.OverflowRoot, coord))
            {
                return false;
            }

            Entry newEntry = new(coord, chunk);
            CommitNewEntry(state, newEntry, firstTombstone, emptySlot);
            return true;
        }

        /// <summary>Inserts or replaces the chunk at <paramref name="coord"/>.</summary>
        /// <param name="coord">The chunk coordinate.</param>
        /// <param name="chunk">The chunk instance to store.</param>
        public void Set(int2 coord, TChunk chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);

            TableState state = _state;
            Entry?[] slots = state.Slots;
            int mask = state.Mask;
            int start = (int)HashCoord(coord) & mask;
            int firstTombstone = -1;
            int emptySlot = -1;

            for (int i = 0; i < MaxProbe; i++)
            {
                int slotIdx = (start + i) & mask;
                Entry? entry = slots[slotIdx];
                if (entry == null)
                {
                    emptySlot = slotIdx;
                    break;
                }

                if (ReferenceEquals(entry, s_tombstone))
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = slotIdx;
                    }
                    continue;
                }

                if (entry.Key == coord)
                {
                    Volatile.Write(ref slots[slotIdx], new Entry(coord, chunk));
                    return;
                }
            }

            if (emptySlot < 0 && state.OverflowRoot != null && TryReplaceOverflow(state, new Entry(coord, chunk)))
            {
                return;
            }

            CommitNewEntry(state, new Entry(coord, chunk), firstTombstone, emptySlot);
        }

        /// <summary>Gets the chunk at <paramref name="coord"/> or creates and stores one using <paramref name="factory"/>.</summary>
        public TChunk GetOrAdd(int2 coord, Func<int2, TChunk> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            return GetOrAdd(coord, factory, static (c, f) => f(c));
        }

        /// <summary>
        /// Gets the chunk at <paramref name="coord"/> or creates and stores one using <paramref name="factory"/> and <paramref name="factoryState"/>.
        /// </summary>
        public TChunk GetOrAdd<TState>(int2 coord, TState factoryState, Func<int2, TState, TChunk> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            TableState state = _state;
            Entry?[] slots = state.Slots;
            int mask = state.Mask;
            int start = (int)HashCoord(coord) & mask;
            int firstTombstone = -1;
            int emptySlot = -1;

            for (int i = 0; i < MaxProbe; i++)
            {
                int slotIdx = (start + i) & mask;
                Entry? entry = slots[slotIdx];
                if (entry == null)
                {
                    emptySlot = slotIdx;
                    break;
                }

                if (ReferenceEquals(entry, s_tombstone))
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = slotIdx;
                    }
                    continue;
                }

                if (entry.Key == coord)
                {
                    return entry.Value;
                }
            }

            if (emptySlot < 0 && state.OverflowRoot != null && TryGetOverflow(state.OverflowRoot, coord, out TChunk? existing))
            {
                return existing;
            }

            TChunk created = factory(coord, factoryState);
            ArgumentNullException.ThrowIfNull(created);
            CommitNewEntry(state, new Entry(coord, created), firstTombstone, emptySlot);
            return created;
        }

        /// <summary>Removes the chunk at <paramref name="coord"/> if present.</summary>
        /// <param name="coord">The chunk coordinate.</param>
        /// <param name="removed">Receives the removed chunk when found; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if removed; otherwise <see langword="false"/>.</returns>
        public bool TryRemove(int2 coord, [NotNullWhen(true)] out TChunk? removed)
        {
            TableState state = _state;
            Entry?[] slots = state.Slots;
            int mask = state.Mask;
            int start = (int)HashCoord(coord) & mask;

            for (int i = 0; i < MaxProbe; i++)
            {
                int slotIdx = (start + i) & mask;
                Entry? entry = slots[slotIdx];
                if (entry == null)
                {
                    removed = null;
                    return false;
                }

                if (!ReferenceEquals(entry, s_tombstone) && entry.Key == coord)
                {
                    removed = entry.Value;
                    Volatile.Write(ref slots[slotIdx], s_tombstone);
                    _tombstoneCount++;
                    Volatile.Write(ref _count, _count - 1);
                    MaybeRehashAfterRemove();
                    return true;
                }
            }

            if (state.OverflowRoot != null && TryRemoveOverflow(state, coord, out removed))
            {
                _overflowCount--;
                if (_overflowCount == 0)
                {
                    Volatile.Write(ref state.OverflowRoot, null);
                }

                Volatile.Write(ref _count, _count - 1);
                MaybeRehashAfterRemove();
                return true;
            }

            removed = null;
            return false;
        }

        /// <summary>Removes all entries from the table and resets capacity to the minimum size.</summary>
        public void Clear()
        {
            Volatile.Write(ref _state, new TableState(MinCapacity));
            Volatile.Write(ref _count, 0);
            _tombstoneCount = 0;
            _overflowCount = 0;
        }

        /// <summary>Returns a value-type enumerator over all entries (writer thread only).</summary>
        public Enumerator GetEnumerator() => new(this);

        /// <summary>Enumerates all entries in a <see cref="ChunkTable{TChunk}"/> without heap allocation unless overflow entries exist.</summary>
        public struct Enumerator
        {
            private readonly Entry?[] _slots;
            private readonly object? _overflowRoot;
            private int _slotIndex;
            private bool _overflowInitialized;
            private Stack<object>? _overflowStack;
            private KeyValuePair<int2, TChunk> _current;

            /// <summary>Initializes an enumerator over <paramref name="table"/>.</summary>
            /// <param name="table">The chunk table to enumerate.</param>
            public Enumerator(ChunkTable<TChunk> table)
            {
                ArgumentNullException.ThrowIfNull(table);
                TableState state = Volatile.Read(ref table._state);
                _slots = state.Slots;
                _overflowRoot = state.OverflowRoot;
                _slotIndex = -1;
                _overflowInitialized = false;
                _overflowStack = null;
                _current = default;
            }

            /// <summary>Gets the current key-value pair.</summary>
            public readonly KeyValuePair<int2, TChunk> Current => _current;

            /// <summary>Advances to the next live entry.</summary>
            public bool MoveNext()
            {
                while (_slotIndex + 1 < _slots.Length)
                {
                    Entry? entry = _slots[++_slotIndex];
                    if (entry != null && !ReferenceEquals(entry, s_tombstone))
                    {
                        _current = new KeyValuePair<int2, TChunk>(entry.Key, entry.Value);
                        return true;
                    }
                }

                if (_overflowRoot == null)
                {
                    return false;
                }

                if (!_overflowInitialized)
                {
                    _overflowInitialized = true;
                    _overflowStack = new Stack<object>(8);
                    _overflowStack.Push(_overflowRoot);
                }

                while (_overflowStack != null && _overflowStack.Count > 0)
                {
                    object node = _overflowStack.Pop();
                    if (node is Entry leaf)
                    {
                        _current = new KeyValuePair<int2, TChunk>(leaf.Key, leaf.Value);
                        return true;
                    }

                    if (node is RadixBranch branch)
                    {
                        for (int i = 15; i >= 0; i--)
                        {
                            object? child = branch.Children[i];
                            if (child != null)
                            {
                                _overflowStack.Push(child);
                            }
                        }
                    }
                }

                return false;
            }
        }

        private void CommitNewEntry(TableState state, Entry newEntry, int firstTombstone, int emptySlot)
        {
            int targetSlot = firstTombstone >= 0 ? firstTombstone : emptySlot;
            if (targetSlot >= 0)
            {
                if (firstTombstone >= 0)
                {
                    _tombstoneCount--;
                }

                Volatile.Write(ref state.Slots[targetSlot], newEntry);
            }
            else
            {
                InsertNewOverflow(state, newEntry);
                _overflowCount++;
            }

            Volatile.Write(ref _count, _count + 1);
            MaybeRehashAfterInsert();
        }

        private void MaybeRehashAfterInsert()
        {
            int cap = _state.Slots.Length;
            if (_count * 8 > cap * 5)
            {
                Rehash(cap << 1);
            }
            else if (_tombstoneCount * 4 > cap)
            {
                Rehash(cap);
            }
        }

        private void MaybeRehashAfterRemove()
        {
            int cap = _state.Slots.Length;
            if (cap > MinCapacity && _count * 8 < cap)
            {
                Rehash(Math.Max(MinCapacity, cap >> 1));
            }
            else if (_tombstoneCount * 4 > cap)
            {
                Rehash(cap);
            }
        }

        private void Rehash(int newCapacity)
        {
            TableState oldState = _state;
            TableState nextState = new(newCapacity);
            int nextOverflowCount = 0;

            Entry?[] oldSlots = oldState.Slots;
            for (int i = 0; i < oldSlots.Length; i++)
            {
                Entry? entry = oldSlots[i];
                if (entry != null && !ReferenceEquals(entry, s_tombstone))
                {
                    InsertRehashEntry(nextState, entry, ref nextOverflowCount);
                }
            }

            if (oldState.OverflowRoot != null)
            {
                RehashOverflowNode(nextState, oldState.OverflowRoot, ref nextOverflowCount);
            }

            Volatile.Write(ref _state, nextState);
            _tombstoneCount = 0;
            _overflowCount = nextOverflowCount;
        }

        private static void RehashOverflowNode(TableState nextState, object node, ref int overflowCount)
        {
            if (node is Entry leaf)
            {
                InsertRehashEntry(nextState, leaf, ref overflowCount);
                return;
            }

            if (node is RadixBranch branch)
            {
                for (int i = 0; i < 16; i++)
                {
                    object? child = branch.Children[i];
                    if (child != null)
                    {
                        RehashOverflowNode(nextState, child, ref overflowCount);
                    }
                }
            }
        }

        private static void InsertRehashEntry(TableState targetState, Entry entry, ref int overflowCount)
        {
            Entry?[] slots = targetState.Slots;
            int mask = targetState.Mask;
            int start = (int)HashCoord(entry.Key) & mask;

            for (int i = 0; i < MaxProbe; i++)
            {
                int slotIdx = (start + i) & mask;
                if (slots[slotIdx] == null)
                {
                    slots[slotIdx] = entry;
                    return;
                }
            }

            InsertNewOverflow(targetState, entry);
            overflowCount++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint HashCoord(int2 coord)
        {
            ulong z = ((ulong)(uint)coord.X << 32) | (uint)coord.Y;
            z ^= z >> 30;
            z *= 0xbf58476d1ce4e5b9UL;
            z ^= z >> 27;
            z *= 0x94d049bb133111ebUL;
            z ^= z >> 31;
            return (uint)z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ChildIndex(int2 coord, int level)
        {
            int shift = 30 - (level << 1);
            uint xBits = ((uint)coord.X >> shift) & 3u;
            uint yBits = ((uint)coord.Y >> shift) & 3u;
            return (int)((xBits << 2) | yBits);
        }

        private static bool TryGetOverflow(object root, int2 coord, [NotNullWhen(true)] out TChunk? chunk)
        {
            uint ux = (uint)coord.X;
            uint uy = (uint)coord.Y;
            object? node = root;

            for (int level = 0; level < RadixLevels; level++)
            {
                if (node is Entry leaf)
                {
                    if (leaf.Key == coord)
                    {
                        chunk = leaf.Value;
                        return true;
                    }

                    chunk = null;
                    return false;
                }

                if (node is not RadixBranch branch)
                {
                    chunk = null;
                    return false;
                }

                int shift = 30 - (level << 1);
                int childIdx = (int)((((ux >> shift) & 3u) << 2) | ((uy >> shift) & 3u));
                node = Volatile.Read(ref branch.Children[childIdx]);
                if (node == null)
                {
                    chunk = null;
                    return false;
                }
            }

            if (node is Entry finalLeaf && finalLeaf.Key == coord)
            {
                chunk = finalLeaf.Value;
                return true;
            }

            chunk = null;
            return false;
        }

        private static bool ContainsOverflow(object root, int2 coord) => TryGetOverflow(root, coord, out _);

        private static bool TryReplaceOverflow(TableState state, Entry newEntry)
        {
            object? node = state.OverflowRoot;
            if (node == null)
            {
                return false;
            }

            if (node is Entry rootLeaf)
            {
                if (rootLeaf.Key != newEntry.Key)
                {
                    return false;
                }

                Volatile.Write(ref state.OverflowRoot, newEntry);
                return true;
            }

            RadixBranch branch = (RadixBranch)node;
            for (int level = 0; level < RadixLevels; level++)
            {
                int childIdx = ChildIndex(newEntry.Key, level);
                object? child = branch.Children[childIdx];
                if (child == null)
                {
                    return false;
                }

                if (child is Entry leaf)
                {
                    if (leaf.Key != newEntry.Key)
                    {
                        return false;
                    }

                    Volatile.Write(ref branch.Children[childIdx], newEntry);
                    return true;
                }

                branch = (RadixBranch)child;
            }

            return false;
        }

        private static void InsertNewOverflow(TableState state, Entry newEntry)
        {
            object? root = state.OverflowRoot;
            if (root == null)
            {
                Volatile.Write(ref state.OverflowRoot, newEntry);
                return;
            }

            if (root is Entry existingRoot)
            {
                RadixBranch splitRoot = CreateSplitSubtree(existingRoot, newEntry, 0);
                Volatile.Write(ref state.OverflowRoot, splitRoot);
                return;
            }

            RadixBranch branch = (RadixBranch)root;
            for (int level = 0; level < RadixLevels; level++)
            {
                int childIdx = ChildIndex(newEntry.Key, level);
                object? child = branch.Children[childIdx];
                if (child == null)
                {
                    Volatile.Write(ref branch.Children[childIdx], newEntry);
                    return;
                }

                if (child is Entry existingLeaf)
                {
                    RadixBranch sub = CreateSplitSubtree(existingLeaf, newEntry, level + 1);
                    Volatile.Write(ref branch.Children[childIdx], sub);
                    return;
                }

                branch = (RadixBranch)child;
            }
        }

        private static bool TryRemoveOverflow(TableState state, int2 coord, [NotNullWhen(true)] out TChunk? removed)
        {
            object? node = state.OverflowRoot;
            if (node == null)
            {
                removed = null;
                return false;
            }

            if (node is Entry rootLeaf)
            {
                if (rootLeaf.Key != coord)
                {
                    removed = null;
                    return false;
                }

                removed = rootLeaf.Value;
                Volatile.Write(ref state.OverflowRoot, null);
                return true;
            }

            RadixBranch branch = (RadixBranch)node;
            for (int level = 0; level < RadixLevels; level++)
            {
                int childIdx = ChildIndex(coord, level);
                object? child = branch.Children[childIdx];
                if (child == null)
                {
                    removed = null;
                    return false;
                }

                if (child is Entry leaf)
                {
                    if (leaf.Key != coord)
                    {
                        removed = null;
                        return false;
                    }

                    removed = leaf.Value;
                    Volatile.Write(ref branch.Children[childIdx], null);
                    return true;
                }

                branch = (RadixBranch)child;
            }

            removed = null;
            return false;
        }

        private static RadixBranch CreateSplitSubtree(Entry existing, Entry incoming, int startLevel)
        {
            RadixBranch top = new();
            RadixBranch current = top;
            int level = startLevel;

            int existingIdx = ChildIndex(existing.Key, level);
            int incomingIdx = ChildIndex(incoming.Key, level);

            while (existingIdx == incomingIdx)
            {
                RadixBranch next = new();
                current.Children[existingIdx] = next;
                current = next;
                level++;
                existingIdx = ChildIndex(existing.Key, level);
                incomingIdx = ChildIndex(incoming.Key, level);
            }

            current.Children[existingIdx] = existing;
            current.Children[incomingIdx] = incoming;
            return top;
        }

        private static int RoundUpCapacity(int requested)
        {
            if (requested <= MinCapacity)
            {
                return MinCapacity;
            }

            uint value = (uint)requested - 1u;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return (int)(value + 1u);
        }
    }

    /// <summary>
    /// Stack-allocated read cursor that caches the last looked-up chunk in a <see cref="ChunkTable{TChunk}"/>.
    /// </summary>
    /// <typeparam name="TChunk">The reference chunk type stored in the table.</typeparam>
    public ref struct ChunkReadCursor<TChunk> where TChunk : class
    {
        private readonly ChunkTable<TChunk> _table;
        private int2 _lastCoord;
        private TChunk? _lastChunk;
        private bool _hasCache;

        /// <summary>Initializes a read cursor bound to <paramref name="table"/>.</summary>
        /// <param name="table">The chunk table to read from.</param>
        public ChunkReadCursor(ChunkTable<TChunk> table)
        {
            ArgumentNullException.ThrowIfNull(table);
            _table = table;
            _lastCoord = default;
            _lastChunk = null;
            _hasCache = false;
        }

        /// <summary>Attempts to retrieve the chunk at <paramref name="coord"/>, reusing the cached result on repeat coordinates.</summary>
        /// <param name="coord">The chunk coordinate.</param>
        /// <param name="chunk">The chunk if present; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if found; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int2 coord, [NotNullWhen(true)] out TChunk? chunk)
        {
            if (_hasCache && _lastCoord == coord)
            {
                chunk = _lastChunk;
                return chunk != null;
            }

            _hasCache = true;
            _lastCoord = coord;
            if (_table.TryGet(coord, out chunk))
            {
                _lastChunk = chunk;
                return true;
            }

            _lastChunk = null;
            return false;
        }

        /// <summary>Clears the cached chunk entry so the next read queries the underlying table.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _hasCache = false;
            _lastChunk = null;
        }
    }
}
