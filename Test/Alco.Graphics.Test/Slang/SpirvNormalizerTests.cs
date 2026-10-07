using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Unit tests for <see cref="SpirvNormalizer"/>, ported one-to-one from the
/// inline tests of alco-gpu's deleted shader_spirv.rs: synthetic modules built
/// from raw words verify wrapper removal, tail expansion with fresh merge
/// labels, hazard rejection, atomicity and idempotence.
/// </summary>
[TestFixture]
public class SpirvNormalizerTests
{
    private const uint Magic = 0x0723_0203;
    private const ushort TypeInt = 21;
    private const ushort Function = 54;
    private const ushort FunctionEnd = 56;
    private const ushort LoopMerge = 246;
    private const ushort SelectionMerge = 247;
    private const ushort LabelOp = 248;
    private const ushort Branch = 249;
    private const ushort BranchConditional = 250;
    private const ushort Switch = 251;

    private static void Emit(List<uint> words, ushort op, params uint[] args)
    {
        words.Add(((uint)(args.Length + 1) << 16) | op);
        words.AddRange(args);
    }

    private static void Label(List<uint> words, uint id) => Emit(words, LabelOp, id);

    private static void BranchTo(List<uint> words, uint target) => Emit(words, Branch, target);

    private static List<uint> Module()
    {
        List<uint> words = [Magic, 0x0001_0300, 0, 100, 0];
        Emit(words, TypeInt, 1, 32, 1);
        Emit(words, 43, 1, 2, 0);
        Emit(words, Function, 3, 4, 0, 5);
        return words;
    }

    private static void Finish(List<uint> words)
    {
        Emit(words, 253);
        Emit(words, FunctionEnd);
    }

    private static List<uint> NestedLoops(bool earlyExit)
    {
        List<uint> words = Module();
        Label(words, 10);
        BranchTo(words, 11);
        Label(words, 11);
        Emit(words, LoopMerge, 30, 29, 0);
        BranchTo(words, 12);
        Label(words, 12);
        Emit(words, SelectionMerge, 27, 0);
        Emit(words, Switch, 2, 13);
        Label(words, 13);
        Emit(words, SelectionMerge, 14, 0);
        Emit(words, BranchConditional, 6, 14, 30);
        Label(words, 14);
        BranchTo(words, 15);
        Label(words, 15);
        Emit(words, LoopMerge, 26, 25, 0);
        BranchTo(words, 16);
        Label(words, 16);
        Emit(words, SelectionMerge, 23, 0);
        Emit(words, Switch, 2, 17);
        Label(words, 17);
        Emit(words, SelectionMerge, 18, 0);
        Emit(words, BranchConditional, 6, 18, 26);
        Label(words, 18);
        if (earlyExit)
        {
            Emit(words, SelectionMerge, 21, 0);
            Emit(words, BranchConditional, 6, 19, 21);
            Label(words, 19);
            BranchTo(words, 23);
            Label(words, 21);
        }
        BranchTo(words, 23);
        Label(words, 23);
        BranchTo(words, 25);
        Label(words, 25);
        BranchTo(words, 15);
        Label(words, 26);
        BranchTo(words, 27);
        Label(words, 27);
        BranchTo(words, 29);
        Label(words, 29);
        BranchTo(words, 11);
        Label(words, 30);
        Finish(words);
        return words;
    }

    private static List<uint> AncestorTail(bool withWork)
    {
        List<uint> words = Module();
        Label(words, 10);
        BranchTo(words, 11);
        Label(words, 11);
        Emit(words, LoopMerge, 30, 29, 0);
        BranchTo(words, 12);
        Label(words, 12);
        Emit(words, SelectionMerge, 27, 0);
        Emit(words, Switch, 2, 13);
        Label(words, 13);
        Emit(words, SelectionMerge, 14, 0);
        Emit(words, BranchConditional, 6, 14, 30);
        Label(words, 14);
        Emit(words, SelectionMerge, 26, 0);
        Emit(words, BranchConditional, 6, 15, 26);
        Label(words, 15);
        Emit(words, SelectionMerge, 18, 0);
        Emit(words, BranchConditional, 6, 17, 18);
        Label(words, 17);
        BranchTo(words, 27);
        Label(words, 18);
        BranchTo(words, 26);
        Label(words, 26);
        if (withWork)
        {
            Emit(words, 128, 1, 50, 2, 2);
        }
        BranchTo(words, 27);
        Label(words, 27);
        BranchTo(words, 29);
        Label(words, 29);
        BranchTo(words, 11);
        Label(words, 30);
        Finish(words);
        return words;
    }

    private static uint[] NormalizeWords(List<uint> words, out int removed)
    {
        byte[] bytes = SpirvNormalizer.Normalize(ToBytes(words), out removed);
        uint[] result = new uint[bytes.Length / 4];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (uint)(bytes[i * 4] | (bytes[i * 4 + 1] << 8)
                | (bytes[i * 4 + 2] << 16) | (bytes[i * 4 + 3] << 24));
        }
        return result;
    }

    private static List<uint> BlockLabels(uint[] words)
    {
        List<uint> labels = [];
        int offset = 5;
        while (offset < words.Length)
        {
            uint word = words[offset];
            if ((ushort)word == LabelOp)
            {
                labels.Add(words[offset + 1]);
            }
            offset += (int)(word >> 16);
        }
        return labels;
    }

    private static bool ContainsSequence(uint[] words, params uint[] pattern)
    {
        for (int offset = 0; offset + pattern.Length <= words.Length; offset++)
        {
            bool matches = true;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (words[offset + i] != pattern[i])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                return true;
            }
        }
        return false;
    }

    private static int FindSequence(List<uint> words, params uint[] pattern)
    {
        for (int offset = 0; offset + pattern.Length <= words.Count; offset++)
        {
            bool matches = true;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (words[offset + i] != pattern[i])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                return offset;
            }
        }
        throw new InvalidOperationException("pattern not found");
    }

    [Test]
    public void NestedLoopBreakWrappersAreRemovedWithoutChangingLabels()
    {
        List<uint> words = NestedLoops(false);
        uint[] original = [.. words];
        uint[] normalized = NormalizeWords(words, out int removed);

        Assert.That(removed, Is.EqualTo(2));
        Assert.That(normalized.Length, Is.EqualTo(original.Length - 8));
        Assert.That(BlockLabels(normalized), Is.EqualTo(BlockLabels(original)));
        Assert.That(NormalizeWords([.. normalized], out int again), Is.EqualTo(normalized));
        Assert.That(again, Is.EqualTo(0));
    }

    [Test]
    public void SourceContinueExpandsSelectionTailWithUniqueMerge()
    {
        List<uint> words = NestedLoops(true);
        uint[] normalized = NormalizeWords(words, out int removed);

        Assert.That(removed, Is.EqualTo(2));
        Assert.That(normalized[3], Is.EqualTo(101u));
        Assert.That(ContainsSequence(
            normalized, (3u << 16) | SelectionMerge, 100, 0), Is.True);
        Assert.That(ContainsSequence(
            normalized, (2u << 16) | LabelOp, 100, (2u << 16) | Branch, 23), Is.True);
        Assert.That(NormalizeWords([.. normalized], out int again), Is.EqualTo(normalized));
        Assert.That(again, Is.EqualTo(0));
    }

    [Test]
    public void PhiBearingEarlyExitMergeKeepsItsSwitch()
    {
        List<uint> words = NestedLoops(true);
        int offset = FindSequence(words, (2u << 16) | LabelOp, 23);
        uint[] phi = [(7u << 16) | 245, 1, 50, 2, 19, 2, 21];
        words.InsertRange(offset + 2, phi);

        Assert.That(() => SpirvNormalizer.Normalize(ToBytes(words)), Throws.TypeOf<ShaderCompilationException>());
        Assert.That(ContainsSequence([.. words], phi), Is.True);
    }

    [Test]
    public void SwitchInsideSelectionEarlyExitRemainsUnsupported()
    {
        List<uint> words = NestedLoops(true);
        int offset = FindSequence(words, (4u << 16) | BranchConditional, 6, 19, 21);
        words.RemoveRange(offset, 4);
        words.InsertRange(offset, new uint[] { (5u << 16) | Switch, 2, 19, 0, 21 });

        Assert.That(() => SpirvNormalizer.Normalize(ToBytes(words)), Throws.TypeOf<ShaderCompilationException>());
    }

    [Test]
    public void SilentAncestorMergePreservesBypassAndHasNoBridgeCycle()
    {
        List<uint> words = AncestorTail(false);
        uint[] normalized = NormalizeWords(words, out int removed);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(ContainsSequence(normalized, (3u << 16) | SelectionMerge, 26, 0), Is.True);
        Assert.That(ContainsSequence(
            normalized, (2u << 16) | LabelOp, 100, (2u << 16) | Branch, 26), Is.True);
        Assert.That(ContainsSequence(
            normalized, (2u << 16) | LabelOp, 26, (2u << 16) | Branch, 27), Is.True);
        Assert.That(ContainsSequence(
            normalized, (2u << 16) | LabelOp, 18, (2u << 16) | Branch, 100), Is.True);
        Assert.That(NormalizeWords([.. normalized], out int again), Is.EqualTo(normalized));
        Assert.That(again, Is.EqualTo(0));
    }

    [Test]
    public void AncestorTailWorkAndSmallIdBoundFailAtomically()
    {
        List<uint> invalidBound = AncestorTail(false);
        invalidBound[3] = 20;
        foreach (List<uint> words in new[] { AncestorTail(true), invalidBound })
        {
            uint[] original = [.. words];
            Assert.That(() => SpirvNormalizer.Normalize(ToBytes(words)), Throws.TypeOf<ShaderCompilationException>());
            Assert.That(words.ToArray(), Is.EqualTo(original));
        }
    }

    [Test]
    public void GenuineSwitchAndNonLoopDefaultSwitchAreUnchanged()
    {
        foreach (uint[] cases in new[] { new uint[] { 2, 11 }, new uint[] { 2, 11, 1, 12 } })
        {
            List<uint> words = Module();
            Label(words, 10);
            Emit(words, SelectionMerge, 13, 0);
            Emit(words, Switch, cases);
            Label(words, 11);
            BranchTo(words, 13);
            if (cases.Length > 2)
            {
                Label(words, 12);
                BranchTo(words, 13);
            }
            Label(words, 13);
            Finish(words);
            uint[] original = [.. words];

            uint[] normalized = NormalizeWords(words, out int removed);
            Assert.That(removed, Is.EqualTo(0));
            Assert.That(normalized, Is.EqualTo(original));
        }
    }

    [Test]
    public void PhiInstructionsAndPredecessorIdsAreUnchanged()
    {
        List<uint> words = NestedLoops(false);
        int labelOffset = FindSequence(words, (2u << 16) | LabelOp, 15);
        uint[] phi = [(7u << 16) | 245, 1, 50, 2, 14, 51, 25];
        words.InsertRange(labelOffset + 2, phi);

        uint[] normalized = NormalizeWords(words, out int removed);
        Assert.That(removed, Is.EqualTo(2));
        Assert.That(ContainsSequence(normalized, phi), Is.True);
    }

    [Test]
    public void IndependentFunctionsNormalizeSeparately()
    {
        List<uint> words = NestedLoops(false);
        List<uint> second = NestedLoops(false);
        int functionOffset = second.IndexOf((5u << 16) | Function);
        List<uint> function = second.GetRange(functionOffset, second.Count - functionOffset);
        // Remap only function/block IDs; shared type, selector, condition, and
        // literal operands retain their original values.
        int offset = 0;
        while (offset < function.Count)
        {
            uint word = function[offset];
            ushort op = (ushort)word;
            int count = (int)(word >> 16);
            switch (op)
            {
                case Function:
                    function[offset + 2] = 104;
                    break;
                case LabelOp or Branch:
                    function[offset + 1] += 100;
                    break;
                case LoopMerge:
                    function[offset + 1] += 100;
                    function[offset + 2] += 100;
                    break;
                case SelectionMerge:
                    function[offset + 1] += 100;
                    break;
                case Switch:
                    function[offset + 2] += 100;
                    break;
                case BranchConditional:
                    function[offset + 2] += 100;
                    function[offset + 3] += 100;
                    break;
            }
            offset += count;
        }
        words[3] = 200;
        words.AddRange(function);

        NormalizeWords(words, out int removed);
        Assert.That(removed, Is.EqualTo(4));
    }

    [Test]
    public void MalformedModulesFailWithoutPanickingOrPartialChanges()
    {
        List<uint> truncated = NestedLoops(false);
        truncated.RemoveAt(truncated.Count - 1);
        List<uint> unknownTarget = NestedLoops(false);
        int offset = FindSequence(unknownTarget, (2u << 16) | Branch, 11);
        unknownTarget[offset + 1] = 999;
        List<uint>[] modules =
        [
            [],
            [Magic, Magic, Magic, Magic],
            [Magic, 0, 0, 0, 0, 0],
            truncated,
            unknownTarget,
        ];
        foreach (List<uint> words in modules)
        {
            uint[] original = [.. words];
            Assert.That(() => SpirvNormalizer.Normalize(ToBytes(words)),
                Throws.TypeOf<ShaderCompilationException>(), $"module of {words.Count} words");
            Assert.That(words.ToArray(), Is.EqualTo(original));
        }
    }

    private static byte[] ToBytes(List<uint> words)
    {
        byte[] bytes = new byte[words.Count * 4];
        for (int i = 0; i < words.Count; i++)
        {
            uint word = words[i];
            bytes[i * 4] = (byte)word;
            bytes[i * 4 + 1] = (byte)(word >> 8);
            bytes[i * 4 + 2] = (byte)(word >> 16);
            bytes[i * 4 + 3] = (byte)(word >> 24);
        }
        return bytes;
    }
}
