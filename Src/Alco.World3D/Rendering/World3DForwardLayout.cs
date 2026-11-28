using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private readonly ForwardLayout _forwardLayout = new();

    private readonly record struct ForwardBucket(string SurfaceModule, int FirstCandidate, int CandidateCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ForwardCandidateGpu(uint InstanceSlot, uint BucketIndex);

    private sealed class ForwardLayout : IDisposable
    {
        private const ulong CandidateBytes = 8;

        private readonly Dictionary<int, string> _slots = [];
        private readonly List<ForwardBucket> _buckets = [];
        private readonly HashSet<GpuBuffer> _payloadPages = [];
        private ForwardCandidateGpu[] _cpuCandidates = [];
        private GpuBuffer? _buffer;
        private int _stagedMaxInstances = -1;
        private bool _dirty;
        private bool _submittedDirty;

        public IReadOnlyList<ForwardBucket> Buckets => _buckets;
        public HashSet<GpuBuffer> PayloadPages => _payloadPages;
        public GpuBuffer? Buffer => _buffer;
        public int CandidateCount { get; private set; }

        public void SetSlot(int instanceSlot, bool isForwardBlend, string surfaceModule)
        {
            if (!isForwardBlend)
            {
                if (_slots.Remove(instanceSlot))
                    _dirty = true;
                return;
            }

            _slots[instanceSlot] = surfaceModule;
            _dirty = true;
        }

        public void Clear()
        {
            _slots.Clear();
            _buckets.Clear();
            _payloadPages.Clear();
            CandidateCount = 0;
            _stagedMaxInstances = -1;
            _dirty = false;
            _submittedDirty = false;
        }

        public bool Stage(RenderContext context, RenderingSystem system, GpuStore gpu, SlotMap<InstanceEntry> instances,
            Dictionary<GeometryEntry, int> stagedLods, int maxInstances)
        {
            if (!_dirty && !_submittedDirty && _stagedMaxInstances == maxInstances)
                return CandidateCount > 0;

            _buckets.Clear();
            _payloadPages.Clear();
            CandidateCount = 0;

            if (_slots.Count == 0 || maxInstances <= 0)
            {
                _dirty = false;
                _submittedDirty = false;
                return false;
            }

            Dictionary<string, List<int>> grouped = [];
            foreach ((int slot, string moduleName) in _slots)
            {
                InstanceEntry? inst = instances.GetSlot(slot);
                if (inst == null)
                    continue;
                GeometryEntry geom = inst.Geometry;
                int staged = stagedLods.GetValueOrDefault(geom);
                bool hasResidentLod = false;
                for (int lod = 0; lod < MaxGeometryLods; ++lod)
                {
                    ResidentLod? entry = (staged & (1 << lod)) != 0
                        ? geom.Pending[lod]!.Value.Replacement
                        : geom.Resident[lod];
                    if (entry is ResidentLod resident)
                    {
                        _payloadPages.Add(resident.Allocation.Buffer);
                        hasResidentLod = true;
                    }
                }
                if (!hasResidentLod)
                    continue;

                if (!grouped.TryGetValue(moduleName, out List<int>? list))
                {
                    if (grouped.Count >= 254)
                        continue;
                    list = [];
                    grouped.Add(moduleName, list);
                }
                list.Add(slot);
            }

            if (grouped.Count == 0)
            {
                _dirty = false;
                _submittedDirty = false;
                return false;
            }

            List<string> orderedModules = [.. grouped.Keys];
            orderedModules.Sort(StringComparer.Ordinal);

            int total = 0;
            foreach (string mod in orderedModules)
            {
                int remaining = Math.Max(0, maxInstances - total);
                if (remaining == 0)
                    break;
                List<int> modSlots = grouped[mod];
                modSlots.Sort();
                int take = Math.Min(modSlots.Count, remaining);
                _buckets.Add(new ForwardBucket(mod, total, take));
                total += take;
            }

            if (total == 0)
            {
                _dirty = false;
                _submittedDirty = false;
                return false;
            }

            if (_cpuCandidates.Length < total)
                _cpuCandidates = new ForwardCandidateGpu[Math.Max(total, 256)];

            int cursor = 0;
            for (int b = 0; b < _buckets.Count; ++b)
            {
                ForwardBucket bucket = _buckets[b];
                List<int> modSlots = grouped[bucket.SurfaceModule];
                for (int i = 0; i < bucket.CandidateCount; ++i)
                    _cpuCandidates[cursor++] = new ForwardCandidateGpu((uint)modSlots[i], (uint)b);
            }

            ulong bytes = (ulong)total * CandidateBytes;
            if (_buffer == null || _buffer.SizeBytes < bytes)
            {
                _buffer?.Dispose();
                _buffer = system.CreateBuffer(Math.Max(bytes, 256UL * CandidateBytes), MemoryType.GpuOnly);
            }

            BufferSlice staging = context.UploadTransient<ForwardCandidateGpu>(_cpuCandidates.AsSpan(0, total));
            gpu.AddCopy(staging, _buffer.Slice(0, bytes));

            CandidateCount = total;
            _stagedMaxInstances = maxInstances;
            _dirty = false;
            _submittedDirty = true;
            return true;
        }

        public void MarkDirtyFromPayloadChange()
        {
            if (_slots.Count > 0)
                _dirty = true;
        }

        public void Reconcile(bool submitted)
        {
            if (submitted)
                _submittedDirty = false;
            else
                _dirty = true;
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
            Clear();
        }
    }
}
