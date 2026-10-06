using System.Numerics;

namespace Alco.Graphics;

// ─────────────────────────────────────────────────────────────────────────────
// Conservative normalization of Slang's default-only switch wrappers before
// Naga. Naga 30's SPIR-V frontend loses the target of an enclosing-loop break
// inside a switch; removing a redundant wrapper before parsing preserves that
// target. Ported one-to-one from alco-gpu's shader_spirv.rs so the managed
// compile pipeline — which owns the slang producer — applies it before the
// bytes reach the GPU library, keeping alco-gpu producer-agnostic.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Rewrites slang-emitted SPIR-V before Naga consumes it on backends that
/// translate SPIR-V instead of passing it through (D3D12). Removes redundant
/// default-only switch wrappers inside loops; source-continue shapes expand a
/// dominance chain of selections through the loop-body tail with fresh, unique
/// merge labels. Existing values and phi predecessors stay unchanged;
/// phi-bearing tail merges and ambiguous shapes without nonlocal loop breaks
/// are preserved, while unsupported nonlocal loop breaks fail before Naga can
/// miscompile them. Valid SPIR-V IDs and their header bound are prerequisites —
/// this pass is not a complete module validator. The input array is never
/// modified; failures throw <see cref="ShaderCompilationException"/>.
/// </summary>
public static class SpirvNormalizer
{
    private const uint Magic = 0x0723_0203;
    private const ushort OpTypeInt = 21;
    private const ushort OpFunction = 54;
    private const ushort OpFunctionEnd = 56;
    private const ushort OpLoopMerge = 246;
    private const ushort OpSelectionMerge = 247;
    private const ushort OpLabel = 248;
    private const ushort OpBranch = 249;
    private const ushort OpBranchConditional = 250;
    private const ushort OpSwitch = 251;

    /// <summary>
    /// Removes redundant default-only switch wrappers inside loops; returns
    /// the normalized bytes (the input reference itself when nothing changed).
    /// </summary>
    /// <param name="spirv">Slang-emitted SPIR-V words as little-endian bytes.</param>
    /// <exception cref="ShaderCompilationException">
    /// The module is malformed or contains an unsupported nonlocal loop break.
    /// </exception>
    public static byte[] Normalize(byte[] spirv) => Normalize(spirv, out _);

    /// <summary>
    /// Removes redundant default-only switch wrappers inside loops; returns
    /// the normalized bytes (the input reference itself when nothing changed).
    /// </summary>
    /// <param name="spirv">Slang-emitted SPIR-V words as little-endian bytes.</param>
    /// <param name="wrappersRemoved">How many wrappers the fixpoint removed.</param>
    /// <exception cref="ShaderCompilationException">
    /// The module is malformed or contains an unsupported nonlocal loop break.
    /// </exception>
    public static byte[] Normalize(byte[] spirv, out int wrappersRemoved)
    {
        uint[] words = ToWords(spirv);
        uint[] output = words;
        int total = 0;
        // First erase wrappers without restructuring. Later passes can then
        // expand a remaining wrapper without overlapping any nested switch.
        foreach (bool allowTailSelections in new[] { false, true })
        {
            while (true)
            {
                int count = NormalizePass(output, allowTailSelections, false, out uint[] next);
                if (count == 0)
                {
                    break;
                }
                total += count;
                output = next;
            }
        }
        NormalizePass(output, true, true, out _);
        wrappersRemoved = total;
        return total == 0 ? spirv : ToBytes(output);
    }

    /// <summary>Decodes one SPIR-V instruction header at the given word offset.</summary>
    private static Instruction DecodeInstruction(uint[] words, int offset)
    {
        if (offset >= words.Length)
        {
            throw Fail("truncated SPIR-V instruction");
        }
        uint word = words[offset];
        int count = (int)(word >> 16);
        if (count == 0 || count > words.Length - offset)
        {
            throw Fail("invalid SPIR-V instruction word count");
        }
        return new Instruction(offset, count, (ushort)word);
    }

    private static ShaderCompilationException Fail(string message) => new(message);

    private static bool ContainsBit(ulong[] bits, int index) => (bits[index >> 6] & (1ul << (index & 63))) != 0;

    private static long DominatorCount(ulong[] bits)
    {
        long count = 0;
        foreach (ulong word in bits)
        {
            count += BitOperations.PopCount(word);
        }
        return count;
    }

    /// <summary>Bitset dominator sets over the reachable part of the CFG.</summary>
    private static ulong[][] Dominators(List<Block> blocks, Dictionary<uint, int> indices)
    {
        bool[] reachable = new bool[blocks.Count];
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.Count > 0)
        {
            int index = pending.Pop();
            if (reachable[index])
            {
                continue;
            }
            reachable[index] = true;
            foreach (uint id in blocks[index].Successors)
            {
                pending.Push(indices[id]);
            }
        }
        ulong[] all = new ulong[(blocks.Count + 63) >> 6];
        for (int index = 0; index < blocks.Count; index++)
        {
            if (reachable[index])
            {
                all[index >> 6] |= 1ul << (index & 63);
            }
        }
        ulong[][] result = new ulong[blocks.Count][];
        for (int index = 0; index < blocks.Count; index++)
        {
            result[index] = (ulong[])all.Clone();
            if (!reachable[index] || index == 0)
            {
                result[index] = new ulong[all.Length];
                if (reachable[index])
                {
                    result[index][index >> 6] |= 1ul << (index & 63);
                }
            }
        }
        while (true)
        {
            bool changed = false;
            for (int index = 1; index < blocks.Count; index++)
            {
                if (!reachable[index])
                {
                    continue;
                }
                ulong[] next = (ulong[])all.Clone();
                foreach (int predecessor in blocks[index].Predecessors)
                {
                    if (reachable[predecessor])
                    {
                        for (int word = 0; word < next.Length; word++)
                        {
                            next[word] &= result[predecessor][word];
                        }
                    }
                }
                next[index >> 6] |= 1ul << (index & 63);
                if (!BitsEqual(next, result[index]))
                {
                    result[index] = next;
                    changed = true;
                }
            }
            if (!changed)
            {
                return result;
            }
        }
    }

    private static bool BitsEqual(ulong[] left, ulong[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Full legality analysis for removing one wrapper; returns the nested tail
    /// selections to expand (possibly empty), or null when the shape is not one
    /// of the deliberately narrow supported cases.
    /// </summary>
    private static List<int>? Eligible(
        int header, int defaultIndex, List<Block> blocks, Dictionary<uint, int> indices,
        ulong[][] dom, bool allowTailSelections)
    {
        int merge = blocks[header].Construct!.Value.Merge;
        List<int> expanded = [];
        if (defaultIndex == header
            || defaultIndex == merge
            || !ContainsBit(dom[defaultIndex], header)
            || !ContainsBit(dom[merge], header)
            || blocks[merge].Predecessors.Count == 0)
        {
            return null;
        }

        HashSet<int> boundaries = [];
        bool inLoop = false;
        for (int index = 0; index < blocks.Count; index++)
        {
            Construct? construct = blocks[index].Construct;
            if (construct == null)
            {
                continue;
            }
            Construct value = construct.Value;
            // Shared merge labels or headers at another construct's boundary need
            // more extensive restructuring than this deliberately narrow pass.
            if (index != header
                && (value.Merge == merge
                    || value.Merge == header
                    || value.Continuing == header
                    || value.Continuing == merge))
            {
                return null;
            }
            if (index != header
                && ContainsBit(dom[header], index)
                && !ContainsBit(dom[header], value.Merge))
            {
                boundaries.Add(value.Merge);
                if (value.Continuing is int continuing)
                {
                    inLoop = true;
                    boundaries.Add(continuing);
                    boundaries.Add(index);
                }
            }
        }
        if (!inLoop || blocks[merge].Construct != null)
        {
            return null;
        }

        // Find the wrapper's region, stopping at its own merge and at legal exits
        // to ancestor constructs. In particular, do not follow an outer loop's
        // backedge and accidentally include the next execution of this wrapper.
        bool[] region = new bool[blocks.Count];
        var pending = new Stack<int>();
        pending.Push(defaultIndex);
        while (pending.Count > 0)
        {
            int index = pending.Pop();
            if (index == merge || boundaries.Contains(index))
            {
                continue;
            }
            if (index == header || !ContainsBit(dom[index], header))
            {
                return null;
            }
            if (region[index])
            {
                continue;
            }
            region[index] = true;
            foreach (uint id in blocks[index].Successors)
            {
                pending.Push(indices[id]);
            }
        }

        uint mergeLabel = blocks[merge].Label;
        foreach (int predecessor in blocks[merge].Predecessors)
        {
            Block block = blocks[predecessor];
            if (!region[predecessor]
                || block.Terminator != OpBranch
                || block.Successors.Count != 1
                || block.Successors[0] != mergeLabel)
            {
                return null;
            }
            // Without the wrapper, an early exit from a nested selection to the
            // wrapper's merge would no longer be a legal structured exit. Require
            // every nested construct that dominates this edge to have finished.
            for (int nested = 0; nested < blocks.Count; nested++)
            {
                Construct? construct = blocks[nested].Construct;
                if (construct == null)
                {
                    continue;
                }
                Construct value = construct.Value;
                if (!region[nested]
                    || !ContainsBit(dom[predecessor], nested)
                    || ContainsBit(dom[predecessor], value.Merge))
                {
                    continue;
                }
                // A break from an enclosing nested loop can legally bypass the
                // merges of that loop's child selections/switches. Its merge must
                // already dominate this predecessor; do not exempt an early exit
                // from a child selection that is still inside the loop.
                bool exitedLoop = false;
                for (int loopHeader = 0; loopHeader < blocks.Count && !exitedLoop; loopHeader++)
                {
                    Construct? loopConstruct = blocks[loopHeader].Construct;
                    if (loopConstruct == null)
                    {
                        continue;
                    }
                    Construct loopValue = loopConstruct.Value;
                    if (region[loopHeader]
                        && loopValue.Continuing != null
                        && ContainsBit(dom[nested], loopHeader)
                        && !ContainsBit(dom[nested], loopValue.Merge)
                        && ContainsBit(dom[predecessor], loopValue.Merge))
                    {
                        exitedLoop = true;
                    }
                }
                if (!exitedLoop)
                {
                    if (!allowTailSelections
                        || blocks[merge].HasPhi
                        || value.Continuing != null
                        || blocks[nested].Terminator != OpBranchConditional
                        || !region[value.Merge]
                        || blocks[value.Merge].HasPhi
                        || !ContainsBit(dom[value.Merge], nested))
                    {
                        return null;
                    }
                    if (!expanded.Contains(nested))
                    {
                        expanded.Add(nested);
                    }
                }
            }
        }
        // Expanded tail selections must form a strict dominance chain. A source
        // continue can skip the rest of that chain, but unrelated branches cannot
        // be combined into the same sequence of fresh merge blocks.
        expanded.Sort((left, right) => DominatorCount(dom[left]).CompareTo(DominatorCount(dom[right])));
        for (int i = 0; i + 1 < expanded.Count; i++)
        {
            if (!ContainsBit(dom[expanded[i + 1]], expanded[i]))
            {
                return null;
            }
        }
        foreach (int nested in expanded)
        {
            int oldMerge = blocks[nested].Construct!.Value.Merge;
            for (int index = 0; index < blocks.Count; index++)
            {
                Construct? construct = blocks[index].Construct;
                if (index != nested
                    && construct != null
                    && (construct.Value.Merge == oldMerge || construct.Value.Continuing == oldMerge))
                {
                    return null;
                }
                // After expansion, every reachable tail block must still be
                // dominated by this selection header. Reject cross-entry tails.
                if (region[index]
                    && ContainsBit(dom[index], oldMerge)
                    && !ContainsBit(dom[index], nested))
                {
                    return null;
                }
            }
        }
        return expanded;
    }

    /// <summary>
    /// Where each expanded selection's fresh merge block must land: the merge of
    /// the deepest enclosing ancestor selection, when all expanded selections
    /// agree on one destination.
    /// </summary>
    private static List<int>? TailDestinations(
        int header, List<int> expanded, List<Block> blocks, Dictionary<uint, int> indices, ulong[][] dom)
    {
        int wrapperMerge = blocks[header].Construct!.Value.Merge;
        List<int> destinations = [];
        foreach (int nested in expanded)
        {
            int destination = wrapperMerge;
            long depth = 0;
            for (int ancestor = 0; ancestor < blocks.Count; ancestor++)
            {
                Construct? construct = blocks[ancestor].Construct;
                if (construct == null)
                {
                    continue;
                }
                Construct value = construct.Value;
                if (ancestor == header
                    || ancestor == nested
                    || expanded.Contains(ancestor)
                    || !ContainsBit(dom[ancestor], header)
                    || !ContainsBit(dom[nested], ancestor)
                    || ContainsBit(dom[nested], value.Merge))
                {
                    continue;
                }
                // Do not expand through a genuine switch or a loop boundary.
                if (value.Continuing != null || blocks[ancestor].Terminator != OpBranchConditional)
                {
                    return null;
                }
                long ancestorDepth = DominatorCount(dom[ancestor]);
                if (ancestorDepth > depth)
                {
                    depth = ancestorDepth;
                    destination = value.Merge;
                }
            }
            if (destination != wrapperMerge)
            {
                int cursor = destination;
                HashSet<int> visited = [];
                while (cursor != wrapperMerge)
                {
                    Block block = blocks[cursor];
                    if (!visited.Add(cursor)
                        || !block.BranchOnly
                        || block.Construct != null
                        || block.HasPhi
                        || block.Successors.Count != 1)
                    {
                        return null;
                    }
                    cursor = indices[block.Successors[0]];
                }
                foreach (int predecessor in blocks[destination].Predecessors)
                {
                    if (ContainsBit(dom[predecessor], nested) && blocks[predecessor].Terminator != OpBranch)
                    {
                        return null;
                    }
                }
            }
            // Moving this selection's merge can invalidate a child's formerly
            // legal early exit to that old merge. Decline such cross-construct
            // entries rather than attempting recursive restructuring.
            int oldMerge = blocks[nested].Construct!.Value.Merge;
            foreach (int predecessor in blocks[oldMerge].Predecessors)
            {
                for (int child = 0; child < blocks.Count; child++)
                {
                    Construct? childConstruct = blocks[child].Construct;
                    if (childConstruct == null)
                    {
                        continue;
                    }
                    if (child != nested
                        && ContainsBit(dom[child], nested)
                        && ContainsBit(dom[predecessor], child)
                        && !ContainsBit(dom[predecessor], childConstruct.Value.Merge))
                    {
                        return null;
                    }
                }
            }
            destinations.Add(destination);
        }
        // Fresh merge chains may share one tail destination. Different ancestor
        // boundaries would require a more general region-nesting transformation.
        for (int i = 0; i + 1 < destinations.Count; i++)
        {
            if (destinations[i] != destinations[i + 1])
            {
                return null;
            }
        }
        return destinations;
    }

    /// <summary>
    /// Whether control inside the wrapper can break an enclosing loop — the
    /// shape Naga miscompiles once the wrapper's merge stops guarding the exit.
    /// </summary>
    private static bool HasEnclosingLoopBreak(
        int header, List<Block> blocks, Dictionary<uint, int> indices, ulong[][] dom)
    {
        int merge = blocks[header].Construct!.Value.Merge;
        HashSet<int> stops = [header, merge];
        HashSet<int> loopMerges = [];
        for (int ancestor = 0; ancestor < blocks.Count; ancestor++)
        {
            Construct? construct = blocks[ancestor].Construct;
            if (construct == null)
            {
                continue;
            }
            Construct value = construct.Value;
            if (ancestor != header
                && ContainsBit(dom[header], ancestor)
                && !ContainsBit(dom[header], value.Merge))
            {
                stops.Add(value.Merge);
                if (value.Continuing is int continuing)
                {
                    loopMerges.Add(value.Merge);
                    stops.Add(continuing);
                    stops.Add(ancestor);
                }
            }
        }
        HashSet<int> visited = [];
        var pending = new Stack<int>();
        pending.Push(indices[blocks[header].Successors[0]]);
        while (pending.Count > 0)
        {
            int index = pending.Pop();
            if (loopMerges.Contains(index))
            {
                return true;
            }
            if (stops.Contains(index) || !visited.Add(index))
            {
                continue;
            }
            foreach (uint id in blocks[index].Successors)
            {
                pending.Push(indices[id]);
            }
        }
        return false;
    }

    /// <summary>CFG construction and the per-function rewrite plan for all wrappers.</summary>
    private static Rewrites FunctionRewrites(
        uint[] words, Instruction[] instructions, Dictionary<uint, uint> integerWidths,
        bool allowTailSelections, bool checkHazards, ref uint nextId)
    {
        List<Block> blocks = [];
        Dictionary<uint, int> indices = [];
        List<(uint Merge, uint? Continuing)?> merges = [];
        for (int position = 0; position < instructions.Length; position++)
        {
            Instruction inst = instructions[position];
            if (inst.Op == OpLabel)
            {
                if (inst.Count != 2 || indices.ContainsKey(words[inst.Offset + 1]))
                {
                    throw Fail("invalid or duplicate SPIR-V block label");
                }
                if (blocks.Count > 0 && blocks[^1].Terminator == 0)
                {
                    throw Fail("SPIR-V block has no terminator");
                }
                indices[words[inst.Offset + 1]] = blocks.Count;
                merges.Add(null);
                blocks.Add(new Block { Label = words[inst.Offset + 1], LabelOffset = inst.Offset });
                continue;
            }
            if (blocks.Count == 0)
            {
                continue;
            }
            Block block = blocks[^1];
            if (inst.Op is not (OpBranch or 0 or 8 or 317))
            {
                block.BranchOnly = false;
            }
            if (inst.Op == 245)
            {
                block.HasPhi = true;
            }
            if (inst.Op == OpLoopMerge || inst.Op == OpSelectionMerge)
            {
                block.MergeOffset = inst.Offset;
                if (inst.Count < (inst.Op == OpLoopMerge ? 4 : 3) || merges[^1] != null)
                {
                    throw Fail("invalid SPIR-V merge instruction");
                }
                merges[^1] = (words[inst.Offset + 1], inst.Op == OpLoopMerge ? words[inst.Offset + 2] : null);
            }
            switch (inst.Op)
            {
                case OpBranch when inst.Count == 2:
                    block.Successors.Add(words[inst.Offset + 1]);
                    break;
                case OpBranchConditional when inst.Count >= 4:
                    block.Successors.Add(words[inst.Offset + 2]);
                    block.Successors.Add(words[inst.Offset + 3]);
                    break;
                case OpSwitch when inst.Count >= 3:
                    block.Successors.Add(words[inst.Offset + 2]);
                    if (inst.Count == 3)
                    {
                        if (position > 0)
                        {
                            Instruction previous = instructions[position - 1];
                            if (previous.Op == OpSelectionMerge && previous.Count == 3)
                            {
                                block.Candidate = (previous.Offset, inst.Offset, words[inst.Offset + 2]);
                            }
                        }
                    }
                    else
                    {
                        if (!integerWidths.TryGetValue(words[inst.Offset + 1], out uint width))
                        {
                            throw Fail("unknown SPIR-V switch selector width");
                        }
                        int stride = width == 64 ? 3 : 2;
                        if ((inst.Count - 3) % stride != 0)
                        {
                            throw Fail("invalid SPIR-V switch cases");
                        }
                        for (int start = inst.Offset + 3; start + stride <= inst.Offset + inst.Count; start += stride)
                        {
                            block.Successors.Add(words[start + stride - 1]);
                        }
                    }
                    break;
                case 252 or 253 or 254 or 255 or 4416 or 4448 or 4449 or 5294:
                    break;
                default:
                    continue;
            }
            if (block.Terminator != 0)
            {
                throw Fail("multiple SPIR-V block terminators");
            }
            block.Terminator = inst.Op;
            block.TerminatorOffset = inst.Offset;
        }
        if (blocks.Count == 0 || blocks[^1].Terminator == 0)
        {
            throw Fail("SPIR-V function has no terminated block");
        }
        for (int index = 0; index < blocks.Count; index++)
        {
            if (merges[index] is { } mergeInfo)
            {
                if (!indices.TryGetValue(mergeInfo.Merge, out int mergeIndex))
                {
                    throw Fail("missing SPIR-V merge block");
                }
                int? continuing = null;
                if (mergeInfo.Continuing is uint continueId)
                {
                    if (!indices.TryGetValue(continueId, out int continueIndex))
                    {
                        throw Fail("missing SPIR-V continue block");
                    }
                    continuing = continueIndex;
                }
                blocks[index].Construct = new Construct(mergeIndex, continuing);
            }
            foreach (uint successor in blocks[index].Successors.ToArray())
            {
                if (!indices.TryGetValue(successor, out int successorIndex))
                {
                    throw Fail("missing SPIR-V branch target");
                }
                blocks[successorIndex].Predecessors.Add(index);
            }
        }
        ulong[][] dom = Dominators(blocks, indices);
        Rewrites rewrites = new();
        // Avoid overlapping expansions in one pass. The caller reparses after each
        // change, so a removed inner wrapper can expose a safe outer wrapper.
        HashSet<int> claimed = [];
        for (int header = 0; header < blocks.Count; header++)
        {
            Block block = blocks[header];
            if (block.Candidate is not { } candidate || block.Construct == null)
            {
                continue;
            }
            if (checkHazards)
            {
                if (HasEnclosingLoopBreak(header, blocks, indices, dom))
                {
                    throw Fail(
                        "unsupported SPIR-V loop break through default-only switch; Naga would lose its target");
                }
                continue;
            }
            List<int>? eligible = Eligible(header, indices[candidate.Default], blocks, indices, dom, allowTailSelections);
            if (eligible == null)
            {
                continue;
            }
            bool overlaps = false;
            foreach (int nested in eligible)
            {
                if (claimed.Contains(nested))
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps)
            {
                continue;
            }
            int merge = block.Construct.Value.Merge;
            List<int> expanded = eligible;
            List<int> allExpanded = [.. expanded];
            expanded.RemoveAll(ancestor => !RetainAncestor(ancestor, merge, allExpanded));

            // An inner expansion must never move a retained ancestor merge with
            // work. Only the proven branch-only ancestor-tail case is supported.
            bool ancestorConflict = false;
            foreach (int ancestor in expanded)
            {
                int ancestorMerge = blocks[ancestor].Construct!.Value.Merge;
                if (blocks[ancestorMerge].BranchOnly)
                {
                    continue;
                }
                foreach (int child in expanded)
                {
                    if (child != ancestor
                        && ContainsBit(dom[child], ancestor)
                        && !ContainsBit(dom[child], ancestorMerge))
                    {
                        ancestorConflict = true;
                        break;
                    }
                }
                if (ancestorConflict)
                {
                    break;
                }
            }
            if (ancestorConflict)
            {
                continue;
            }
            List<int>? destinations = TailDestinations(header, expanded, blocks, indices, dom);
            if (destinations == null)
            {
                continue;
            }
            if (expanded.Count > 0)
            {
                for (int index = 0; index < blocks.Count; index++)
                {
                    if (blocks[index].Label >= words[3])
                    {
                        throw Fail("SPIR-V ID bound does not include all block labels");
                    }
                }
            }
            List<uint> newMerges = [];
            foreach (int nested in expanded)
            {
                uint id = nextId;
                if (nextId == uint.MaxValue)
                {
                    throw Fail("SPIR-V ID bound overflow");
                }
                nextId++;
                newMerges.Add(id);
                rewrites.Operands.Add((blocks[nested].MergeOffset!.Value + 1, id));
                claimed.Add(nested);
            }
            if (expanded.Count > 0)
            {
                int destination = destinations[0];
                HashSet<int> silentTail = [];
                int tail = destination;
                while (tail != merge)
                {
                    silentTail.Add(tail);
                    tail = indices[blocks[tail].Successors[0]];
                }
                List<int> predecessors = [.. blocks[merge].Predecessors];
                if (destination != merge)
                {
                    predecessors.AddRange(blocks[destination].Predecessors);
                }
                foreach (int predecessor in predecessors)
                {
                    if (silentTail.Contains(predecessor))
                    {
                        continue;
                    }
                    int position = -1;
                    for (int i = 0; i < expanded.Count; i++)
                    {
                        if (ContainsBit(dom[predecessor], expanded[i]))
                        {
                            position = i;
                        }
                    }
                    if (position >= 0)
                    {
                        rewrites.Operands.Add((blocks[predecessor].TerminatorOffset + 1, newMerges[position]));
                    }
                }
                List<(uint Label, uint Target)> bridgeList = [];
                for (int position = expanded.Count - 1; position >= 0; position--)
                {
                    uint target = position == 0 ? blocks[destination].Label : newMerges[position - 1];
                    bridgeList.Add((newMerges[position], target));
                }
                rewrites.Bridges.Add((blocks[destination].LabelOffset, bridgeList));
            }
            rewrites.Wrappers.Add(candidate);
        }
        return rewrites;

        // Keeps an ancestor expansion only while its fresh merge is still needed:
        // either the tail from its merge to the wrapper merge is not silent, or
        // some early exit into it is not already legalized by a descendant.
        bool RetainAncestor(int ancestor, int merge, List<int> allExpanded)
        {
            int ancestorMerge = blocks[ancestor].Construct!.Value.Merge;
            int cursor = ancestorMerge;
            HashSet<int> visited = [];
            while (cursor != merge)
            {
                Block tail = blocks[cursor];
                if (!visited.Add(cursor)
                    || !tail.BranchOnly
                    || tail.Construct != null
                    || tail.Successors.Count != 1)
                {
                    return true;
                }
                cursor = indices[tail.Successors[0]];
            }
            // A descendant's fresh merge before this silent ancestor tail
            // already turns all of the ancestor's early exits into legal exits.
            foreach (int predecessor in blocks[merge].Predecessors)
            {
                bool finished = !ContainsBit(dom[predecessor], ancestor)
                    || ContainsBit(dom[predecessor], ancestorMerge)
                    || AnyDescendantCovers(predecessor, ancestor, allExpanded);
                if (!finished)
                {
                    return true;
                }
            }
            return false;
        }

        bool AnyDescendantCovers(int predecessor, int ancestor, List<int> allExpanded)
        {
            foreach (int child in allExpanded)
            {
                if (child != ancestor
                    && ContainsBit(dom[child], ancestor)
                    && ContainsBit(dom[predecessor], child))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>One module-level pass; returns the wrapper count and the rewritten words.</summary>
    private static int NormalizePass(uint[] words, bool allowTailSelections, bool checkHazards, out uint[] output)
    {
        if (words.Length < 5 || words[0] != Magic)
        {
            throw Fail("invalid SPIR-V header");
        }
        int offset = 5;
        Instruction previous = default;
        bool hasPrevious = false;
        bool hasCandidate = false;
        while (offset < words.Length)
        {
            Instruction inst = DecodeInstruction(words, offset);
            hasCandidate |= inst.Op == OpSwitch
                && inst.Count == 3
                && hasPrevious
                && previous.Op == OpSelectionMerge
                && previous.Count == 3;
            offset += inst.Count;
            previous = inst;
            hasPrevious = true;
        }
        if (!hasCandidate)
        {
            output = words;
            return 0;
        }

        List<Instruction> instructions = [];
        offset = 5;
        while (offset < words.Length)
        {
            Instruction inst = DecodeInstruction(words, offset);
            offset += inst.Count;
            instructions.Add(inst);
        }
        Dictionary<uint, uint> integerTypes = [];
        foreach (Instruction inst in instructions)
        {
            if (inst.Op == OpTypeInt && inst.Count == 4)
            {
                integerTypes[words[inst.Offset + 1]] = words[inst.Offset + 2];
            }
        }
        Dictionary<uint, uint> integerWidths = [];
        int? functionStart = null;
        List<(int Start, int End)> functions = [];
        for (int index = 0; index < instructions.Count; index++)
        {
            Instruction inst = instructions[index];
            if (inst.Op == OpFunction)
            {
                if (inst.Count != 5 || functionStart != null)
                {
                    throw Fail("invalid SPIR-V function start");
                }
                functionStart = index;
            }
            // In valid SPIR-V, an integer type ID in the first operand of a
            // function instruction identifies a typed result. Global constants
            // are the other possible integer switch selectors.
            if (inst.Count >= 3
                && (functionStart != null || inst.Op == 1 || (inst.Op >= 41 && inst.Op <= 52)))
            {
                if (integerTypes.TryGetValue(words[inst.Offset + 1], out uint width))
                {
                    integerWidths[words[inst.Offset + 2]] = width;
                }
            }
            if (inst.Op == OpFunctionEnd)
            {
                if (functionStart is not int start)
                {
                    throw Fail("unexpected SPIR-V function end");
                }
                functions.Add((start + 1, index));
                functionStart = null;
            }
        }
        if (functionStart != null)
        {
            throw Fail("unterminated SPIR-V function");
        }
        Rewrites rewrites = new();
        uint nextId = words[3];
        foreach ((int start, int end) in functions)
        {
            bool adjacent = false;
            for (int i = start; i + 1 < end; i++)
            {
                if (instructions[i].Op == OpSelectionMerge
                    && instructions[i].Count == 3
                    && instructions[i + 1].Op == OpSwitch
                    && instructions[i + 1].Count == 3)
                {
                    adjacent = true;
                    break;
                }
            }
            if (!adjacent)
            {
                continue;
            }
            Instruction[] body = new Instruction[end - start];
            for (int i = start; i < end; i++)
            {
                body[i - start] = instructions[i];
            }
            Rewrites function = FunctionRewrites(
                words, body, integerWidths, allowTailSelections, checkHazards, ref nextId);
            rewrites.Wrappers.AddRange(function.Wrappers);
            rewrites.Operands.AddRange(function.Operands);
            rewrites.Bridges.AddRange(function.Bridges);
        }
        if (rewrites.Wrappers.Count == 0)
        {
            output = words;
            return 0;
        }
        rewrites.Wrappers.Sort((left, right) => left.MergeOffset.CompareTo(right.MergeOffset));
        Dictionary<int, uint> operands = [];
        foreach ((int operandOffset, uint word) in rewrites.Operands)
        {
            operands[operandOffset] = word;
        }
        Dictionary<int, List<(uint Label, uint Target)>> bridges = [];
        foreach ((int labelOffset, List<(uint Label, uint Target)> list) in rewrites.Bridges)
        {
            bridges[labelOffset] = list;
        }
        List<uint> rewritten = new(words.Length);
        for (int i = 0; i < 5; i++)
        {
            rewritten.Add(words[i]);
        }
        rewritten[3] = nextId;
        int wrapperIndex = 0;
        offset = 5;
        while (offset < words.Length)
        {
            if (bridges.TryGetValue(offset, out List<(uint Label, uint Target)>? inserted))
            {
                foreach ((uint label, uint target) in inserted)
                {
                    rewritten.Add((2u << 16) | OpLabel);
                    rewritten.Add(label);
                    rewritten.Add((2u << 16) | OpBranch);
                    rewritten.Add(target);
                }
            }
            if (wrapperIndex < rewrites.Wrappers.Count)
            {
                (int mergeOffset, int switchOffset, uint defaultTarget) = rewrites.Wrappers[wrapperIndex];
                if (offset == mergeOffset)
                {
                    rewritten.Add((2u << 16) | OpBranch);
                    rewritten.Add(defaultTarget);
                    offset = switchOffset + 3;
                    wrapperIndex++;
                    continue;
                }
            }
            Instruction inst = DecodeInstruction(words, offset);
            for (int index = 0; index < inst.Count; index++)
            {
                int wordOffset = offset + index;
                rewritten.Add(operands.TryGetValue(wordOffset, out uint word) ? word : words[wordOffset]);
            }
            offset += inst.Count;
        }
        output = rewritten.ToArray();
        return rewrites.Wrappers.Count;
    }

    /// <summary>Decodes little-endian bytes to words, dropping an incomplete trailing word.</summary>
    private static uint[] ToWords(byte[] bytes)
    {
        uint[] words = new uint[bytes.Length / 4];
        for (int i = 0; i < words.Length; i++)
        {
            int b = i * 4;
            words[i] = (uint)(bytes[b] | (bytes[b + 1] << 8) | (bytes[b + 2] << 16) | (bytes[b + 3] << 24));
        }
        return words;
    }

    /// <summary>Encodes words back to little-endian bytes.</summary>
    private static byte[] ToBytes(uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            uint word = words[i];
            int b = i * 4;
            bytes[b] = (byte)word;
            bytes[b + 1] = (byte)(word >> 8);
            bytes[b + 2] = (byte)(word >> 16);
            bytes[b + 3] = (byte)(word >> 24);
        }
        return bytes;
    }

    private readonly struct Instruction(int offset, int count, ushort op)
    {
        public readonly int Offset = offset;
        public readonly int Count = count;
        public readonly ushort Op = op;
    }

    private readonly struct Construct(int merge, int? continuing)
    {
        public readonly int Merge = merge;
        public readonly int? Continuing = continuing;
    }

    private sealed class Block
    {
        public uint Label { get; init; }
        public List<uint> Successors { get; } = [];
        public List<int> Predecessors { get; } = [];
        public Construct? Construct { get; set; }
        public ushort Terminator { get; set; }
        public int TerminatorOffset { get; set; }
        public int LabelOffset { get; init; }
        public int? MergeOffset { get; set; }
        public bool HasPhi { get; set; }
        public bool BranchOnly { get; set; } = true;
        public (int MergeOffset, int SwitchOffset, uint Default)? Candidate { get; set; }
    }

    private sealed class Rewrites
    {
        public List<(int MergeOffset, int SwitchOffset, uint Default)> Wrappers { get; } = [];
        public List<(int Offset, uint Word)> Operands { get; } = [];
        public List<(int LabelOffset, List<(uint Label, uint Target)> Pair)> Bridges { get; } = [];
    }
}
