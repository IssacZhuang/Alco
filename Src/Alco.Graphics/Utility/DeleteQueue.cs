// Deferred GPU resource deletion queue.
// Entries hold the deletion Action directly.

using System.Diagnostics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

public sealed class DeleteQueue
{
    private struct Entry
    {
        public ulong RetireValue;
        public Action? Callback;
    }

    private GPUTimelineSemaphore _timeline;
    private Entry[] _entries;
    private uint _capacity;
    private uint _head;
    private uint _tail;
    private uint _count;
    private bool _ticking;

    // Capacity must be nonzero, every callback must run before destruction, and the timeline must be live when Tick() is called.
    public DeleteQueue(GPUTimelineSemaphore timeline, uint capacity)
    {
        Debug.Assert(timeline != null);
        Debug.Assert(capacity != 0);
        _timeline = timeline;
        _entries = new Entry[capacity];
        _capacity = capacity;
    }

    ~DeleteQueue()
    {
        Debug.Assert(_count == 0, "delete queue destroyed with pending callbacks");
    }

    // Retire values must be enqueued in nondecreasing order.
    public void Defer(ulong retireValue, Action callback)
    {
        Debug.Assert(_count < _capacity, "delete queue capacity exhausted");

        Debug.Assert(_count == 0 || _entries[_tail == 0 ? _capacity - 1 : _tail - 1].RetireValue <= retireValue,
                     "delete queue retire values must be nondecreasing");

        _entries[_tail].RetireValue = retireValue;
        _entries[_tail].Callback = callback;
        _tail = _tail + 1 == _capacity ? 0 : _tail + 1;
        ++_count;
    }

    public void Tick()
    {
        if (_count == 0)
            return;

        Collect(_timeline.CompletedValue);
    }

    // Call after WaitIdle() and before destroying the device; runs every queued callback without checking the timeline.
    public void Drain()
    {
        Collect(ulong.MaxValue); // ~uint64{0} in the C++ source
    }

    private void Collect(ulong completedValue) // Callbacks cannot recursively collect the same queue.
    {
        Debug.Assert(!_ticking, "delete queue callbacks cannot recursively collect the same queue");
        _ticking = true;
        while (_count != 0)
        {
            Entry entry = _entries[_head];
            if (entry.RetireValue > completedValue)
                break;

            entry.Callback!();
            _entries[_head].Callback = null;
            _head = _head + 1 == _capacity ? 0 : _head + 1;
            --_count;
        }
        _ticking = false;
    }
}
