using Alco.Graphics;
using Alco.Graphics.Spirv;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Unit tests for <see cref="SpirvReflector"/> that verify reflection of synthesized
/// SPIR-V modules produces the correct engine types (bind groups, push constants,
/// vertex inputs, compute local size).
/// </summary>
[TestFixture]
public class SpirvReflectorTests
{
    private const uint Magic = 0x07230203;
    private const uint Version = 0x00010300;

    private static byte[] BuildModule(uint bound, params uint[][] instructions)
    {
        int totalWords = 5;
        foreach (uint[] inst in instructions)
        {
            totalWords += inst.Length;
        }

        uint[] words = new uint[totalWords];
        words[0] = Magic;
        words[1] = Version;
        words[2] = 0;
        words[3] = bound;
        words[4] = 0;

        int offset = 5;
        foreach (uint[] inst in instructions)
        {
            Array.Copy(inst, 0, words, offset, inst.Length);
            offset += inst.Length;
        }

        byte[] result = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, result, 0, result.Length);
        return result;
    }

    private static uint[] Inst(ushort opCode, params uint[] operands)
    {
        uint[] words = new uint[operands.Length + 1];
        words[0] = ((uint)(operands.Length + 1) << 16) | opCode;
        Array.Copy(operands, 0, words, 1, operands.Length);
        return words;
    }

    // OpName helper that encodes a string into SPIR-V literal words
    private static uint[] OpName(uint target, string name)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        int wordCount = (bytes.Length + 3) / 4;
        uint[] stringWords = new uint[wordCount];
        for (int i = 0; i < bytes.Length; i++)
        {
            stringWords[i / 4] |= (uint)bytes[i] << ((i % 4) * 8);
        }

        uint[] result = new uint[2 + wordCount];
        result[0] = ((uint)(2 + wordCount) << 16) | (ushort)SpirvOp.Name;
        result[1] = target;
        Array.Copy(stringWords, 0, result, 2, wordCount);
        return result;
    }

    // ─── Type Chain Builders ─────────────────────────────────────

    // %result = OpTypeFloat width
    private static uint[] TypeFloat(uint result, uint width)
        => Inst((ushort)SpirvOp.TypeFloat, result, width);

    // %result = OpTypeVector elementID componentCount
    private static uint[] TypeVector(uint result, uint elementID, uint count)
        => Inst((ushort)SpirvOp.TypeVector, result, elementID, count);

    // %result = OpTypeImage sampledType dim depth arrayed ms sampled [format]
    private static uint[] TypeImage(uint result, uint sampledTypeID,
        uint dim, uint depth, uint arrayed, uint multisampled, uint sampled, uint format = 0)
        => Inst((ushort)SpirvOp.TypeImage, result, sampledTypeID, dim, depth, arrayed, multisampled, sampled, format);

    // %result = OpTypeSampler
    private static uint[] TypeSampler(uint result)
        => Inst((ushort)SpirvOp.TypeSampler, result);

    // %result = OpTypeSampledImage imageType
    private static uint[] TypeSampledImage(uint result, uint imageType)
        => Inst((ushort)SpirvOp.TypeSampledImage, result, imageType);

    // %result = OpTypePointer storageClass type
    private static uint[] TypePointer(uint result, uint storageClass, uint typeID)
        => Inst((ushort)SpirvOp.TypePointer, result, storageClass, typeID);

    // %result = OpTypeStruct memberType...
    private static uint[] TypeStructProper(uint result, params uint[] memberTypes)
    {
        uint[] words = new uint[memberTypes.Length + 2];
        words[0] = ((uint)(memberTypes.Length + 2) << 16) | (ushort)SpirvOp.TypeStruct;
        words[1] = result;
        for (int i = 0; i < memberTypes.Length; i++)
        {
            words[2 + i] = memberTypes[i];
        }

        return words;
    }

    // %result = OpTypeRuntimeArray elementType
    private static uint[] TypeRuntimeArray(uint result, uint elementType)
        => Inst((ushort)SpirvOp.TypeRuntimeArray, result, elementType);

    // %result = OpVariable pointerType storageClass [initializer]
    private static uint[] Variable(uint pointerTypeID, uint result, uint storageClass)
        => Inst((ushort)SpirvOp.Variable, pointerTypeID, result, storageClass);

    // ─── Decoration Helpers ──────────────────────────────────────

    private static uint[] Decorate(uint target, SpirvDecoration dec, uint value)
        => Inst((ushort)SpirvOp.Decorate, target, (uint)dec, value);

    private static uint[] MemberDecorate(uint structID, uint memberIndex, SpirvDecoration dec, uint value)
        => Inst((ushort)SpirvOp.MemberDecorate, structID, memberIndex, (uint)dec, value);

    private static uint[] DecorateNoValue(uint target, SpirvDecoration dec)
        => Inst((ushort)SpirvOp.Decorate, target, (uint)dec);

    // ─── Tests ───────────────────────────────────────────────────

    [Test(Description = "Reflects a uniform buffer binding with correct set/binding/size")]
    public void Reflect_UniformBuffer_CorrectBinding()
    {
        // %1 = float32
        // %2 = vec4<float>
        // %3 = struct { vec4 }
        // %4 = ptr<Uniform, %3>
        // %5 = var %4 Uniform
        // Decorations: %3 Block, %5 DescriptorSet=0 Binding=2, %3 member 0 Offset=0
        byte[] spirv = BuildModule(
            10,
            // Types
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypeStructProper(3, 2),
            TypePointer(4, (uint)SpirvStorageClass.Uniform, 3),
            // Variable
            Variable(4, 5, (uint)SpirvStorageClass.Uniform),
            // Decorations
            DecorateNoValue(3, SpirvDecoration.Block),
            MemberDecorate(3, 0, SpirvDecoration.Offset, 0),
            Decorate(5, SpirvDecoration.DescriptorSet, 0),
            Decorate(5, SpirvDecoration.Binding, 2),
            // Name
            OpName(5, "_ubo"),
            // Entry point: Vertex %6 "main"
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Vertex, 6, 0x6E69616D), // "main"
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.BindGroups.Count, Is.EqualTo(1));
        Assert.That(info.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(info.BindGroups[0].Bindings.Count, Is.EqualTo(1));

        BindGroupEntryInfo binding = info.BindGroups[0].Bindings[0];
        Assert.That(binding.Entry.Binding, Is.EqualTo(2u));
        Assert.That(binding.Entry.Type, Is.EqualTo(BindingType.UniformBuffer));
        Assert.That(binding.Entry.Name, Is.EqualTo("_ubo"));
        Assert.That(binding.Size, Is.EqualTo(16u)); // vec4 = 16 bytes
    }

    [Test(Description = "Reflects a texture binding and identifies depth texture via Image Depth operand")]
    public void Reflect_DepthTexture_IdentifiedByDepthOperand()
    {
        // %1 = float
        // %2 = OpTypeImage %1 Dim2D Depth=1 Arrayed=0 MS=0 Sampled=1
        // %3 = ptr<UniformConstant, %2>
        // %4 = var %3 UniformConstant
        byte[] spirv = BuildModule(
            10,
            TypeFloat(1, 32),
            // OpTypeImage: sampledType(1) result(2) dim(Dim2D=1) depth(1) arrayed(0) ms(0) sampled(1) format(Unknown=0)
            TypeImage(2, 1, (uint)SpirvDim.Dim2D, 1, 0, 0, 1),
            TypePointer(3, (uint)SpirvStorageClass.UniformConstant, 2),
            Variable(3, 4, (uint)SpirvStorageClass.UniformConstant),
            Decorate(4, SpirvDecoration.DescriptorSet, 0),
            Decorate(4, SpirvDecoration.Binding, 0),
            OpName(4, "_depthTex"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 5, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.BindGroups[0].Bindings[0].Entry.Type, Is.EqualTo(BindingType.Texture));
        Assert.That(info.BindGroups[0].Bindings[0].Entry.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Depth));
        Assert.That(info.BindGroups[0].Bindings[0].Entry.TextureInfo.ViewDimension, Is.EqualTo(TextureViewDimension.Texture2D));
    }

    [Test(Description = "A non-depth texture has Float sample type")]
    public void Reflect_NormalTexture_HasFloatSampleType()
    {
        byte[] spirv = BuildModule(
            10,
            TypeFloat(1, 32),
            // depth=2 (unknown), sampled=1
            TypeImage(2, 1, (uint)SpirvDim.Dim2D, 2, 0, 0, 1),
            TypePointer(3, (uint)SpirvStorageClass.UniformConstant, 2),
            Variable(3, 4, (uint)SpirvStorageClass.UniformConstant),
            Decorate(4, SpirvDecoration.DescriptorSet, 0),
            Decorate(4, SpirvDecoration.Binding, 0),
            OpName(4, "_colorTex"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 5, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.BindGroups[0].Bindings[0].Entry.TextureInfo.SampleType,
            Is.EqualTo(TextureSampleType.Float));
    }

    [Test(Description = "Reflects a compute shader local size from OpExecutionMode")]
    public void Reflect_ComputeShader_LocalSize()
    {
        byte[] spirv = BuildModule(
            20,
            // %1 = TypeVoid, %2 = TypeFunction, %3 = Function, %4 = Label (not needed for test)
            Inst((ushort)SpirvOp.TypeVoid, 1),
            Inst((ushort)SpirvOp.TypeFunction, 2, 1),
            Inst((ushort)SpirvOp.Function, 1, 3, 0, 2),
            // EntryPoint: GLCompute %3 "main"
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.GLCompute, 3, 0x6E69616D),
            // ExecutionMode: LocalSize 8 4 1
            Inst((ushort)SpirvOp.ExecutionMode, 3, (uint)SpirvExecutionMode.LocalSize, 8, 4, 1),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.Size.X, Is.EqualTo(8u));
        Assert.That(info.Size.Y, Is.EqualTo(4u));
        Assert.That(info.Size.Z, Is.EqualTo(1u));
    }

    [Test(Description = "Reflects a vertex shader with two float4 input variables")]
    public void Reflect_VertexInputs_StrideAndElements()
    {
        // %1 = TypeFloat 32
        // %2 = TypeVector %1 4
        // %3 = ptr<Input, %2>
        // %4 = var %3 Input (location 0)
        // %5 = var %3 Input (location 1)
        byte[] spirv = BuildModule(
            20,
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypePointer(3, (uint)SpirvStorageClass.Input, 2),
            Variable(3, 4, (uint)SpirvStorageClass.Input),
            Variable(3, 5, (uint)SpirvStorageClass.Input),
            Decorate(4, SpirvDecoration.Location, 0),
            Decorate(5, SpirvDecoration.Location, 1),
            OpName(4, "_position"),
            OpName(5, "_color"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Vertex, 6, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.VertexLayouts.Count, Is.EqualTo(1));
        VertexInputLayout layout = info.VertexLayouts[0];
        Assert.That(layout.Stride, Is.EqualTo(32u)); // 2 * vec4(16)
        Assert.That(layout.Elements.Length, Is.EqualTo(2));
        Assert.That(layout.Elements[0].Location, Is.EqualTo(0u));
        Assert.That(layout.Elements[0].Offset, Is.EqualTo(0u));
        Assert.That(layout.Elements[0].Format, Is.EqualTo(VertexFormat.Float32x4));
        Assert.That(layout.Elements[1].Location, Is.EqualTo(1u));
        Assert.That(layout.Elements[1].Offset, Is.EqualTo(16u));
        Assert.That(layout.Elements[1].Format, Is.EqualTo(VertexFormat.Float32x4));
    }

    [Test(Description = "Reflects push constant range from PushConstant storage variable")]
    public void Reflect_PushConstant_CorrectRange()
    {
        // %1 = float32
        // %2 = vec4<float>
        // %3 = struct { float offset(0), vec4 offset(16) }
        // %4 = ptr<PushConstant, %3>
        // %5 = var %4 PushConstant
        byte[] spirv = BuildModule(
            10,
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypeStructProper(3, 1, 2),
            TypePointer(4, (uint)SpirvStorageClass.PushConstant, 3),
            Variable(4, 5, (uint)SpirvStorageClass.PushConstant),
            MemberDecorate(3, 0, SpirvDecoration.Offset, 0),
            MemberDecorate(3, 1, SpirvDecoration.Offset, 16),
            OpName(5, "_pushConstants"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 6, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.PushConstantsRanges.Count, Is.EqualTo(1));
        Assert.That(info.PushConstantsRanges[0].Start, Is.EqualTo(0u));
        Assert.That(info.PushConstantsRanges[0].End, Is.EqualTo(32u)); // float(4) + vec4(16) = 20, but offset+size = 16+16=32
        Assert.That(info.PushConstantsSize, Is.EqualTo(32));
    }

    [Test(Description = "Multiple descriptor sets produce multiple bind groups")]
    public void Reflect_MultipleSets_SeparateBindGroups()
    {
        // Create two texture variables in different sets.
        byte[] spirv = BuildModule(
            20,
            TypeFloat(1, 32),
            TypeImage(2, 1, (uint)SpirvDim.Dim2D, 0, 0, 0, 1),
            TypePointer(3, (uint)SpirvStorageClass.UniformConstant, 2),
            Variable(3, 4, (uint)SpirvStorageClass.UniformConstant),
            Variable(3, 5, (uint)SpirvStorageClass.UniformConstant),
            Decorate(4, SpirvDecoration.DescriptorSet, 0),
            Decorate(4, SpirvDecoration.Binding, 0),
            Decorate(5, SpirvDecoration.DescriptorSet, 1),
            Decorate(5, SpirvDecoration.Binding, 0),
            OpName(4, "_tex0"),
            OpName(5, "_tex1"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 6, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.BindGroups.Count, Is.EqualTo(2));
        Assert.That(info.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(info.BindGroups[1].Group, Is.EqualTo(1u));
    }

    [Test(Description = "Sampler binding is reflected as BindingType.Sampler")]
    public void Reflect_Sampler_CorrectType()
    {
        byte[] spirv = BuildModule(
            20,
            TypeSampler(1),
            TypePointer(2, (uint)SpirvStorageClass.UniformConstant, 1),
            Variable(2, 3, (uint)SpirvStorageClass.UniformConstant),
            Decorate(3, SpirvDecoration.DescriptorSet, 0),
            Decorate(3, SpirvDecoration.Binding, 1),
            OpName(3, "_samp"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 4, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.BindGroups[0].Bindings[0].Entry.Type, Is.EqualTo(BindingType.Sampler));
    }

    [Test(Description = "Storage buffer (StructuredBuffer) reflected as StorageBuffer")]
    public void Reflect_StorageBuffer_CorrectType()
    {
        // %1 = float32
        // %2 = struct { float }
        // %3 = runtimeArray of %2
        // %4 = struct { %3 }
        // %5 = ptr<StorageBuffer, %4>
        // %6 = var %5 StorageBuffer
        byte[] spirv = BuildModule(
            20,
            TypeFloat(1, 32),
            TypeStructProper(2, 1),
            TypeRuntimeArray(3, 2),
            TypeStructProper(4, 3),
            TypePointer(5, (uint)SpirvStorageClass.StorageBuffer, 4),
            Variable(5, 6, (uint)SpirvStorageClass.StorageBuffer),
            DecorateNoValue(4, SpirvDecoration.BufferBlock),
            MemberDecorate(4, 0, SpirvDecoration.Offset, 0),
            MemberDecorate(2, 0, SpirvDecoration.Offset, 0),
            Decorate(3, SpirvDecoration.ArrayStride, 4),
            Decorate(6, SpirvDecoration.DescriptorSet, 0),
            Decorate(6, SpirvDecoration.Binding, 0),
            OpName(6, "_sb"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 7, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.BindGroups[0].Bindings[0].Entry.Type, Is.EqualTo(BindingType.StorageBuffer));
    }

    // ─── Fragment Output Count ──────────────────────────────────

    private static byte[] BuildFragmentModuleWithOutputs(params (uint Id, uint Location)[] outputs)
    {
        List<uint[]> instructions = new()
        {
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypePointer(3, (uint)SpirvStorageClass.Output, 2)
        };

        foreach ((uint id, uint location) in outputs)
        {
            instructions.Add(Variable(3, id, (uint)SpirvStorageClass.Output));
            instructions.Add(Decorate(id, SpirvDecoration.Location, location));
            instructions.Add(OpName(id, $"_out{location}"));
        }

        instructions.Add(Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 9, 0x6E69616D));
        instructions.Add(Inst((ushort)SpirvOp.MemoryModel, 0, 1));

        return BuildModule(10, instructions.ToArray());
    }

    [Test(Description = "A fragment module with one output variable reflects output count 1")]
    public void Reflect_FragmentOutput_SingleOutput()
    {
        byte[] spirv = BuildFragmentModuleWithOutputs((4, 0));

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.FragmentOutputCount, Is.EqualTo(1));
    }

    [Test(Description = "A fragment module with outputs at location 0 and 1 reflects output count 2")]
    public void Reflect_FragmentOutput_MultipleOutputs()
    {
        byte[] spirv = BuildFragmentModuleWithOutputs((4, 0), (5, 1));

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.FragmentOutputCount, Is.EqualTo(2));
    }

    [Test(Description = "BuiltIn outputs (FragDepth) are not color outputs and do not raise the count")]
    public void Reflect_FragmentOutput_BuiltInIgnored()
    {
        byte[] spirv = BuildModule(
            10,
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypePointer(3, (uint)SpirvStorageClass.Output, 2),
            TypePointer(4, (uint)SpirvStorageClass.Output, 1),
            Variable(3, 5, (uint)SpirvStorageClass.Output),
            Variable(4, 6, (uint)SpirvStorageClass.Output),
            Decorate(5, SpirvDecoration.Location, 0),
            Decorate(6, SpirvDecoration.BuiltIn, 1), // FragDepth
            OpName(5, "_color"),
            OpName(6, "_depth"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Fragment, 9, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.FragmentOutputCount, Is.EqualTo(1));
    }

    [Test(Description = "Vertex module outputs are stage varyings, not fragment color outputs: count stays 0")]
    public void Reflect_FragmentOutput_VertexModuleStaysZero()
    {
        byte[] spirv = BuildModule(
            10,
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypePointer(3, (uint)SpirvStorageClass.Output, 2),
            Variable(3, 4, (uint)SpirvStorageClass.Output),
            Decorate(4, SpirvDecoration.Location, 0),
            OpName(4, "_vtxOut"),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Vertex, 9, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        ShaderReflectionInfo info = ShaderReflectionUtility.GetSpirvReflection(spirv);

        Assert.That(info.FragmentOutputCount, Is.EqualTo(0));
    }

    [Test(Description = "Merging vertex and fragment reflections keeps the fragment output count")]
    public void MergeReflectionInfo_KeepsFragmentOutputCount()
    {
        byte[] vertexSpirv = BuildModule(
            10,
            TypeFloat(1, 32),
            TypeVector(2, 1, 4),
            TypePointer(3, (uint)SpirvStorageClass.Output, 2),
            Variable(3, 4, (uint)SpirvStorageClass.Output),
            Decorate(4, SpirvDecoration.Location, 0),
            Inst((ushort)SpirvOp.EntryPoint, (uint)SpirvExecutionModel.Vertex, 9, 0x6E69616D),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );
        byte[] fragmentSpirv = BuildFragmentModuleWithOutputs((4, 0), (5, 1));

        ShaderReflectionInfo vertex = ShaderReflectionUtility.GetSpirvReflection(vertexSpirv);
        ShaderReflectionInfo fragment = ShaderReflectionUtility.GetSpirvReflection(fragmentSpirv);
        ShaderReflectionInfo merged = ShaderReflectionUtility.MergeReflectionInfo(vertex, fragment);

        Assert.That(vertex.FragmentOutputCount, Is.EqualTo(0));
        Assert.That(fragment.FragmentOutputCount, Is.EqualTo(2));
        Assert.That(merged.FragmentOutputCount, Is.EqualTo(2));
    }
}
