using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Linked-program disk cache payload (plan §4.2 layer b): per-entry SPIR-V plus
// the materialized ShaderReflectionInfo and uniform-member tables, so a cached
// program restores without invoking the slang front end at all. The cache key
// (module IR hash, entry set, specialization, slang build tag) is computed by
// SlangModuleSystem; this type is only the value.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A fully linked program in serializable form.</summary>
public sealed record SlangCachedProgram
{
    public required byte[][] EntryCode { get; init; }
    public required (string Name, int Stage)[] EntryPoints { get; init; }
    public required ShaderReflectionInfo Reflection { get; init; }

    /// <summary>Uniform members per constant-buffer block name (material parameter packing).</summary>
    public required IReadOnlyDictionary<string, List<SlangUniformMember>> UniformMembers { get; init; }
}

internal static class SlangProgramCacheCodec
{
    private const int FormatVersion = 1;

    public static void Encode(System.IO.BinaryWriter writer, SlangCachedProgram program)
    {
        writer.Write(FormatVersion);

        writer.Write(program.EntryPoints.Length);
        for (int i = 0; i < program.EntryPoints.Length; i++)
        {
            writer.Write(program.EntryPoints[i].Name);
            writer.Write(program.EntryPoints[i].Stage);
            byte[] code = program.EntryCode[i];
            writer.Write(code.Length);
            writer.Write(code);
        }

        EncodeReflection(writer, program.Reflection);

        writer.Write(program.UniformMembers.Count);
        foreach (KeyValuePair<string, List<SlangUniformMember>> block in program.UniformMembers)
        {
            writer.Write(block.Key);
            writer.Write(block.Value.Count);
            foreach (SlangUniformMember member in block.Value)
            {
                writer.Write(member.Name);
                writer.Write(member.OffsetBytes);
                writer.Write(member.SizeBytes);
                writer.Write(member.FloatComponentCount);
            }
        }
    }

    public static SlangCachedProgram Decode(System.IO.BinaryReader reader)
    {
        int version = reader.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidOperationException($"Unsupported slang program cache format version {version}.");

        int entryCount = reader.ReadInt32();
        (string, int)[] entryPoints = new (string, int)[entryCount];
        byte[][] entryCode = new byte[entryCount][];
        for (int i = 0; i < entryCount; i++)
        {
            string name = reader.ReadString();
            int stage = reader.ReadInt32();
            int length = reader.ReadInt32();
            entryPoints[i] = (name, stage);
            entryCode[i] = reader.ReadBytes(length);
        }

        ShaderReflectionInfo reflection = DecodeReflection(reader);

        int blockCount = reader.ReadInt32();
        Dictionary<string, List<SlangUniformMember>> members = new(blockCount);
        for (int i = 0; i < blockCount; i++)
        {
            string blockName = reader.ReadString();
            int memberCount = reader.ReadInt32();
            List<SlangUniformMember> block = new(memberCount);
            for (int j = 0; j < memberCount; j++)
            {
                block.Add(new SlangUniformMember(
                    reader.ReadString(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadInt32()));
            }
            members[blockName] = block;
        }

        return new SlangCachedProgram
        {
            EntryCode = entryCode,
            EntryPoints = entryPoints,
            Reflection = reflection,
            UniformMembers = members,
        };
    }

    private static void EncodeReflection(System.IO.BinaryWriter writer, ShaderReflectionInfo info)
    {
        writer.Write(info.VertexLayouts.Count);
        foreach (VertexInputLayout layout in info.VertexLayouts)
        {
            writer.Write(layout.Stride);
            writer.Write((int)layout.StepMode);
            writer.Write(layout.Elements.Length);
            foreach (VertexElement element in layout.Elements)
            {
                writer.Write(element.Location);
                writer.Write(element.Offset);
                writer.Write((int)element.Format);
                writer.Write(element.Name);
            }
        }

        writer.Write(info.BindGroups.Count);
        foreach (BindGroupLayout group in info.BindGroups)
        {
            writer.Write(group.Group);
            writer.Write(group.Bindings.Count);
            foreach (BindGroupEntryInfo binding in group.Bindings)
            {
                writer.Write(binding.Entry.Binding);
                writer.Write((int)binding.Entry.Stage);
                writer.Write((int)binding.Entry.Type);
                writer.Write((int)binding.Entry.TextureInfo.ViewDimension);
                writer.Write((int)binding.Entry.TextureInfo.SampleType);
                writer.Write((int)binding.Entry.StorageTextureInfo.Access);
                writer.Write((int)binding.Entry.StorageTextureInfo.ViewDimension);
                writer.Write((int)binding.Entry.StorageTextureInfo.Format);
                writer.Write(binding.Size);
                writer.Write(binding.Entry.Name);
            }
        }

        writer.Write(info.PushConstantsRanges.Count);
        foreach (PushConstantsRange range in info.PushConstantsRanges)
        {
            writer.Write(range.Start);
            writer.Write(range.End);
        }

        writer.Write(info.Size.X);
        writer.Write(info.Size.Y);
        writer.Write(info.Size.Z);
        writer.Write(info.FragmentOutputCount);
    }

    private static ShaderReflectionInfo DecodeReflection(System.IO.BinaryReader reader)
    {
        int layoutCount = reader.ReadInt32();
        VertexInputLayout[] layouts = new VertexInputLayout[layoutCount];
        for (int i = 0; i < layoutCount; i++)
        {
            uint stride = reader.ReadUInt32();
            var stepMode = (VertexStepMode)reader.ReadInt32();
            int elementCount = reader.ReadInt32();
            VertexElement[] elements = new VertexElement[elementCount];
            for (int j = 0; j < elementCount; j++)
            {
                elements[j] = new VertexElement(
                    reader.ReadUInt32(), reader.ReadUInt32(), (VertexFormat)reader.ReadInt32(), reader.ReadString());
            }
            layouts[i] = new VertexInputLayout(elements, stride, stepMode);
        }

        int groupCount = reader.ReadInt32();
        BindGroupLayout[] groups = new BindGroupLayout[groupCount];
        for (int i = 0; i < groupCount; i++)
        {
            uint group = reader.ReadUInt32();
            int bindingCount = reader.ReadInt32();
            BindGroupEntryInfo[] bindings = new BindGroupEntryInfo[bindingCount];
            for (int j = 0; j < bindingCount; j++)
            {
                uint slot = reader.ReadUInt32();
                var stage = (ShaderStage)reader.ReadInt32();
                var type = (BindingType)reader.ReadInt32();
                var textureInfo = new TextureBindingInfo(
                    (TextureViewDimension)reader.ReadInt32(), (TextureSampleType)reader.ReadInt32());
                var storageInfo = new StorageTextureBindingInfo
                {
                    Access = (AccessMode)reader.ReadInt32(),
                    ViewDimension = (TextureViewDimension)reader.ReadInt32(),
                    Format = (PixelFormat)reader.ReadInt32(),
                };
                uint size = reader.ReadUInt32();
                string name = reader.ReadString();
                bindings[j] = new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(slot, stage, type, textureInfo, storageInfo, name),
                    Size = size,
                };
            }
            groups[i] = new BindGroupLayout { Group = group, Bindings = bindings };
        }

        int rangeCount = reader.ReadInt32();
        PushConstantsRange[] ranges = new PushConstantsRange[rangeCount];
        for (int i = 0; i < rangeCount; i++)
        {
            ranges[i] = new PushConstantsRange(reader.ReadUInt32(), reader.ReadUInt32());
        }

        var threadGroupSize = new ThreadGroupSize(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        int fragmentOutputCount = reader.ReadInt32();

        return new ShaderReflectionInfo(layouts, groups, ranges, threadGroupSize, fragmentOutputCount);
    }
}
