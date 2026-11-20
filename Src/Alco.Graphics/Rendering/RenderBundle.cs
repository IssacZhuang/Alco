// Portable draw recordings with immutable root snapshots, prepared state runs, and retained resource bindings.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A reusable draw recording; seal before replay and destroy only after its final recorded and submitted use.</summary>
/// <remarks>Captures root bytes and bindings, not buffer or texture contents.
/// Referenced resources must remain undestroyed through the final replay's completion.</remarks>
public sealed unsafe class RenderBundle : RenderingResource
{
    private struct DrawRecord
    {
        internal int RootOffset;
        internal int RootSize;
        internal GPURange Indices;
        internal uint Count;
        internal uint InstanceCount;
        internal uint First;
        internal int VertexOffset;
        internal uint FirstInstance;
        internal IndexType IndexType;
        internal bool Indexed;
    }

    private struct StateRun
    {
        internal GPUPipeline Pipeline;
        internal DepthStencilState DepthStencil;
        internal int DrawEnd;
    }

    private readonly Format[] ColorFormats;
    private readonly Format DepthFormat;
    private readonly Format StencilFormat;
    private readonly List<RenderingResource> Resources = new(32);
    private DrawRecord[] Draws = [];
    private StateRun[] Runs = [];
    private byte[] RootData = [];
    private int DrawLength;
    private int RunCount;
    private int RootSize;
    private GPUHeap RootHeap;
    private bool Sealed;
    private Material? RetainedResourceOwner;
    private uint RetainedResourceVersion;
    private Buffer? RetainedIndices;

    /// <summary>Starts recording for the given attachment formats; recording is externally synchronized.</summary>
    /// <param name="system">The rendering system supplying the device and descriptor heaps.</param>
    /// <param name="colorFormats">The color attachment formats of compatible passes.</param>
    /// <param name="depthFormat">The depth attachment format, or undefined when absent.</param>
    /// <param name="stencilFormat">The stencil attachment format, or undefined when absent.</param>
    public RenderBundle(RenderingSystem system, ReadOnlySpan<Format> colorFormats,
        Format depthFormat = Format.Undefined, Format stencilFormat = Format.Undefined) : base(system)
    {
        ColorFormats = colorFormats.ToArray();
        DepthFormat = depthFormat;
        StencilFormat = stencilFormat;
    }

    /// <summary>The number of recorded draws.</summary>
    public int DrawCount => DrawLength;

    /// <summary>Records a draw with a snapshot of the material's root data, bindings, and state; prepares its pipeline if needed.</summary>
    /// <param name="material">The material to capture; do not mutate it concurrently with recording.</param>
    /// <param name="vertexCount">The number of vertices per instance.</param>
    /// <param name="instanceCount">The number of instances.</param>
    /// <param name="firstVertex">The index of the first vertex.</param>
    /// <param name="firstInstance">The index of the first instance.</param>
    /// <exception cref="GPUException">The pipeline could not be created.</exception>
    public void Draw(Material material, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        int rootOffset = PrepareDraw(material);
        Draws[DrawLength++] = new DrawRecord
        {
            RootOffset = rootOffset,
            RootSize = (int)material.Root.SizeBytes,
            Count = vertexCount,
            InstanceCount = instanceCount,
            First = firstVertex,
            FirstInstance = firstInstance,
        };
    }

    /// <summary>Records an indexed draw with a snapshot of the material and index range; prepares its pipeline if needed.</summary>
    /// <param name="material">The material to capture; do not mutate it concurrently with recording.</param>
    /// <param name="indices">The retained index buffer; its contents are not copied.</param>
    /// <param name="type">The index element type.</param>
    /// <param name="indexCount">The number of indices per instance.</param>
    /// <param name="instanceCount">The number of instances.</param>
    /// <param name="firstIndex">The first index relative to the captured range.</param>
    /// <param name="vertexOffset">The offset added to each fetched index.</param>
    /// <param name="firstInstance">The index of the first instance.</param>
    /// <param name="byteOffset">The byte offset into the index buffer.</param>
    /// <exception cref="GPUException">The pipeline could not be created.</exception>
    public void DrawIndexed(Material material, Buffer indices, IndexType type, uint indexCount, uint instanceCount = 1,
        uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0, ulong byteOffset = 0)
    {
        Debug.Assert(!indices.IsDestroyed && indices.System == _system && byteOffset <= indices.SizeBytes);
        int rootOffset = PrepareDraw(material);
        if (RetainedIndices != indices)
        {
            Resources.Add(indices);
            RetainedIndices = indices;
        }
        Draws[DrawLength++] = new DrawRecord
        {
            RootOffset = rootOffset,
            RootSize = (int)material.Root.SizeBytes,
            Indices = indices.Range.Slice(byteOffset),
            Count = indexCount,
            InstanceCount = instanceCount,
            First = firstIndex,
            VertexOffset = vertexOffset,
            FirstInstance = firstInstance,
            IndexType = type,
            Indexed = true,
        };
    }

    /// <summary>Seals the recording and uploads its persistent roots; the immutable result can replay concurrently into independent contexts.</summary>
    /// <exception cref="GPUException">The persistent root heap could not be created; the recording remains unsealed.</exception>
    public void EndRecording()
    {
        Debug.Assert(!IsDestroyed && !Sealed);
        if (RootSize != 0)
        {
            GPUHeap heap = _system.Device.CreateGPUHeap((ulong)RootSize);
            RootData.AsSpan(0, RootSize).CopyTo(heap.Range.CpuSpan);
            RootHeap = heap;
        }
        Array.Resize(ref Draws, DrawLength);
        Array.Resize(ref Runs, RunCount);
        Resources.TrimExcess();
        RootData = [];
        RetainedResourceOwner = null;
        RetainedIndices = null;
        Sealed = true;
    }

    internal bool IsCompatible(RenderingSystem system, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat)
    {
        return !IsDestroyed && Sealed && system == _system && DepthFormat == depthFormat && StencilFormat == stencilFormat &&
            ColorFormats.AsSpan().SequenceEqual(colorFormats);
    }

    internal void Replay(RenderContext context, GPUCommandBuffer commands)
    {
        GPURange roots = RootHeap.Range.GPURange;
        int firstDraw = 0;
        for (int runIndex = 0; runIndex < RunCount; ++runIndex)
        {
            ref readonly StateRun run = ref Runs[runIndex];
            context.ApplyDrawState(run.Pipeline, run.DepthStencil);
            for (int drawIndex = firstDraw; drawIndex < run.DrawEnd; ++drawIndex)
            {
                ref readonly DrawRecord draw = ref Draws[drawIndex];
                GPURange root = roots.Slice((ulong)draw.RootOffset, (ulong)draw.RootSize);
                if (draw.Indexed)
                {
                    commands.DrawIndexed(root, draw.Indices, draw.IndexType, draw.Count, draw.InstanceCount,
                        draw.First, draw.VertexOffset, draw.FirstInstance);
                }
                else
                {
                    commands.Draw(root, draw.Count, draw.InstanceCount, draw.First, draw.FirstInstance);
                }
            }
            firstDraw = run.DrawEnd;
        }
    }

    /// <summary>Releases the persistent root heap and captured references without destroying the referenced resources.</summary>
    protected override void Release()
    {
        RootHeap.Destroy();
        RootHeap = default;
        Resources.Clear();
        Draws = [];
        Runs = [];
        RootData = [];
        RetainedResourceOwner = null;
        RetainedIndices = null;
    }

    private int PrepareDraw(Material material)
    {
        Debug.Assert(!IsDestroyed && !Sealed && material.Shader.System == _system && material.Shader.Kind == ShaderKind.Graphics);
        GPUPipeline pipeline = material.GetPipeline(ColorFormats, DepthFormat, StencilFormat);
        DepthStencilState depthStencil = material.DepthStencilState;
        if (RunCount == 0 || Runs[RunCount - 1].Pipeline != pipeline ||
            !RenderContext.DepthStencilMatches(Runs[RunCount - 1].DepthStencil, depthStencil))
        {
            if (RunCount == Runs.Length)
                Array.Resize(ref Runs, Math.Max(4, Runs.Length * 2));
            Runs[RunCount++] = new StateRun { Pipeline = pipeline, DepthStencil = depthStencil };
        }
        Runs[RunCount - 1].DrawEnd = DrawLength + 1;
        if (DrawLength == Draws.Length)
            Array.Resize(ref Draws, Math.Max(64, Draws.Length * 2));

        int rootSize = (int)material.Root.SizeBytes;
        int rootOffset = RootSize;
        RootSize += (rootSize + 15) & ~15;
        if (RootSize > RootData.Length)
            Array.Resize(ref RootData, Math.Max(RootSize, RootData.Length * 2));
        material.CopyRoot(RootData.AsSpan(rootOffset, rootSize));
        Material resourceOwner = material.ResourceOwner;
        uint resourceVersion = resourceOwner.ResourceVersion;
        if (RetainedResourceOwner != resourceOwner || RetainedResourceVersion != resourceVersion)
        {
            resourceOwner.RetainResources(Resources);
            RetainedResourceOwner = resourceOwner;
            RetainedResourceVersion = resourceVersion;
        }
        return rootOffset;
    }
}
