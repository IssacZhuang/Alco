// Lowers the pinned Slang compiler's NoGraphics buffer bindings to Vulkan 1.2 device addresses.

using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Graphics.Slang;

internal sealed class SpirvCompactBindings
{
    private const int HeaderWords = 5;
    private const uint Int64 = 11, PhysicalStorageBufferAddresses = 5347, PhysicalStorageBuffer64 = 5348;
    private const uint UniformConstant = 0, PushConstant = 9, StorageBuffer = 12, PhysicalStorageBuffer = 5349;
    private const uint Block = 2, NonWritable = 24, Binding = 33, DescriptorSet = 34, Offset = 35, Aligned = 2;
    private const uint VariablePointersStorageBuffer = 4441, VariablePointers = 4442, UntypedPointersKhr = 4473, DescriptorHeapExt = 5128;
    private const uint MeshShadingNv = 5266, MeshShadingExt = 5283, BindlessTextureNv = 5390;

    private enum Op : ushort
    {
        Name = 5, Line = 8, MemoryModel = 14, EntryPoint = 15, Capability = 17, TypeInt = 21, TypeFloat = 22, TypeVector = 23,
        TypeMatrix = 24, TypeImage = 25, TypeSampler = 26, TypeArray = 28, TypeRuntimeArray = 29, TypeStruct = 30,
        TypePointer = 32, Constant = 43, Function = 54, FunctionEnd = 56, FunctionCall = 57, Variable = 59, Load = 61,
        Store = 62, CopyMemory = 63, CopyMemorySized = 64, AccessChain = 65, InBoundsAccessChain = 66, PtrAccessChain = 67,
        ArrayLength = 68, InBoundsPtrAccessChain = 70,
        Decorate = 71, MemberDecorate = 72, CopyObject = 83, ConvertUToPtr = 120, Label = 248, NoLine = 317,
    }

    private enum Resource : byte { Other, Root, Table, SampledImage, StorageImage, Sampler }

    private struct Id
    {
        public Resource Resource;
        public uint Set;
        public uint BindingPlusOne;
        public bool ReadOnly;
        public uint Type;
        public uint Alignment;
        public uint PhysicalType;
        public uint FunctionBase;
    }

    private readonly Id[] _ids;
    private readonly string _entryPoint;
    private readonly List<uint> _newPhysicalPointees = [];
    private readonly List<uint> _functionBuffers = [];
    private readonly List<uint> _output = [];
    private uint _nextId;
    private uint _uint32;
    private uint _uint64;
    private bool _hasInt64;
    private bool _hasPhysicalAddresses;
    private bool _hasRawBuffers;
    private uint _pushVariable;
    private uint _pushAddressType;
    private uint _rootMember;
    private uint _tableMember;

    private SpirvCompactBindings(uint bound, string entryPoint)
    {
        _ids = new Id[bound];
        _entryPoint = entryPoint;
        _nextId = bound;
    }

    internal static byte[] Lower(byte[] code, string entryPoint)
    {
        Span<uint> words = MemoryMarshal.Cast<byte, uint>(code.AsSpan());
        if (code.Length % sizeof(uint) != 0 || words.Length < HeaderWords || words[0] != 0x07230203 || words[1] != 0x00010500 || words[3] == 0)
            throw new ShaderCompilationException($"Vulkan Compact shader '{entryPoint}' requires SPIR-V 1.5.");

        SpirvCompactBindings lowering = new(words[3], entryPoint);
        lowering.ScanModule(words[HeaderWords..]);
        if (!lowering._hasRawBuffers)
        {
            lowering.RemapDescriptorSets(words[HeaderWords..]);
            return code;
        }

        lowering.PlanPhysicalPointers(words[HeaderWords..]);
        return lowering.EmitModule(words);
    }

    private void ScanModule(ReadOnlySpan<uint> remaining)
    {
        while (!remaining.IsEmpty)
        {
            ReadOnlySpan<uint> instruction = Next(ref remaining, out Op op);
            switch (op)
            {
                case Op.Capability:
                    _hasInt64 |= instruction[1] == Int64;
                    _hasPhysicalAddresses |= instruction[1] == PhysicalStorageBufferAddresses;
                    if (instruction[1] is VariablePointersStorageBuffer or VariablePointers or UntypedPointersKhr or DescriptorHeapExt or
                        MeshShadingNv or MeshShadingExt or BindlessTextureNv)
                        throw Unsupported($"SPIR-V capability {instruction[1]} is outside the compact binding profile.");
                    break;
                case Op.Decorate:
                    ref Id decorated = ref _ids[instruction[1]];
                    if (instruction[2] == DescriptorSet) decorated.Set = instruction[3];
                    else if (instruction[2] == Binding) decorated.BindingPlusOne = instruction[3] + 1;
                    else if (instruction[2] == NonWritable) decorated.ReadOnly = true;
                    break;
                case Op.TypeInt:
                case Op.TypeFloat:
                    _ids[instruction[1]].Alignment = instruction[2] / 8;
                    if (op == Op.TypeInt && instruction[3] == 0)
                    {
                        if (instruction[2] == 32) _uint32 = instruction[1];
                        if (instruction[2] == 64) _uint64 = instruction[1];
                    }
                    break;
                case Op.TypeImage when instruction[3] < 5 && instruction[7] is 1 or 2:
                    _ids[instruction[1]].Resource = instruction[7] == 1 ? Resource.SampledImage : Resource.StorageImage;
                    break;
                case Op.TypeSampler:
                    _ids[instruction[1]].Resource = Resource.Sampler;
                    break;
                case Op.TypeRuntimeArray:
                    _ids[instruction[1]].Type = instruction[2];
                    goto case Op.TypeArray;
                case Op.TypeVector:
                case Op.TypeMatrix:
                case Op.TypeArray:
                    _ids[instruction[1]].Alignment = _ids[instruction[2]].Alignment;
                    break;
                case Op.TypeStruct:
                    ref uint alignment = ref _ids[instruction[1]].Alignment;
                    foreach (uint member in instruction[2..]) alignment = Math.Max(alignment, _ids[member].Alignment);
                    break;
                case Op.TypePointer:
                    _ids[instruction[1]].Type = instruction[3];
                    if (instruction[2] == PhysicalStorageBuffer)
                        _ids[instruction[3]].PhysicalType = instruction[1];
                    break;
                case Op.Variable:
                    uint id = instruction[2];
                    ref Id variable = ref _ids[id];
                    if (instruction[3] == PushConstant)
                        throw Unsupported("The push-constant block is reserved for root and buffer-table addresses.");
                    if (variable.BindingPlusOne == 0) break;
                    if (variable.Set == 1 && instruction[3] == UniformConstant)
                    {
                        uint array = _ids[instruction[1]].Type;
                        Resource resource = _ids[_ids[array].Type].Resource;
                        if ((variable.BindingPlusOne, resource) is (1, Resource.Sampler) or (3, Resource.SampledImage) or (4, Resource.StorageImage))
                        {
                            variable.Resource = resource;
                            break;
                        }
                    }
                    if (variable.Set == 0 && instruction[3] == StorageBuffer && variable.ReadOnly && variable.BindingPlusOne is 1 or 4)
                    {
                        variable.Resource = variable.BindingPlusOne == 1 ? Resource.Root : Resource.Table;
                        variable.Type = instruction[1];
                        _hasRawBuffers = true;
                        break;
                    }
                    throw Unsupported($"Descriptor set {variable.Set}, binding {variable.BindingPlusOne - 1} is not supported by the compact binding ABI.");
                case Op.Function:
                    return;
            }
        }
    }

    private void PlanPhysicalPointers(ReadOnlySpan<uint> remaining)
    {
        for (uint id = 1; id < _ids.Length; ++id)
        {
            if (IsRawBuffer(id)) PlanPhysicalType(_ids[_ids[id].Type].Type);
        }

        while (!remaining.IsEmpty)
        {
            ReadOnlySpan<uint> instruction = Next(ref remaining, out Op op);
            if (op is Op.AccessChain or Op.InBoundsAccessChain && IsRawBuffer(instruction[3]))
            {
                uint pointee = _ids[instruction[1]].Type;
                uint alignment = _ids[pointee].Alignment;
                if (alignment == 0) throw Unsupported("Cannot determine the scalar alignment of a root or buffer-table load.");
                _ids[instruction[2]].Alignment = alignment;
                PlanPhysicalType(pointee);
            }
            else if ((op is Op.AccessChain or Op.InBoundsAccessChain or Op.PtrAccessChain or Op.InBoundsPtrAccessChain or Op.ArrayLength or Op.CopyObject &&
                      IsRawPointer(instruction[3])) ||
                     (op is Op.Store or Op.CopyMemory or Op.CopyMemorySized && (IsRawPointer(instruction[1]) || IsRawPointer(instruction[2]))))
                throw Unsupported("Unsupported access to a root or buffer-table pointer.");
            else if (op == Op.FunctionCall)
            {
                foreach (uint argument in instruction[4..])
                {
                    if (IsRawPointer(argument)) throw Unsupported("A root or buffer-table pointer cannot escape to another function.");
                }
            }
        }
    }

    private bool IsRawBuffer(uint id) => _ids[id].Resource is Resource.Root or Resource.Table;

    private bool IsRawPointer(uint id) => IsRawBuffer(id) || _ids[id].Alignment != 0;

    private void PlanPhysicalType(uint pointee)
    {
        if (_ids[pointee].PhysicalType != 0) return;
        _ids[pointee].PhysicalType = _nextId++;
        _newPhysicalPointees.Add(pointee);
    }

    private uint EmittedSet(uint id) => _ids[id].Resource switch
    {
        Resource.Sampler => 3,
        Resource.StorageImage => 2,
        _ => _ids[id].Set,
    };

    private void RemapDescriptorSets(Span<uint> words)
    {
        for (int i = 0; i < words.Length; i += (int)(words[i] >> 16))
        {
            Op op = (Op)words[i];
            if (op == Op.Function) break;
            if (op == Op.Decorate && words[i + 2] == DescriptorSet) words[i + 3] = EmittedSet(words[i + 1]);
        }
    }

    private byte[] EmitModule(ReadOnlySpan<uint> words)
    {
        _output.EnsureCapacity(words.Length + 128);
        _output.AddRange(words[..HeaderWords]);
        if (!_hasPhysicalAddresses) Emit(Op.Capability, PhysicalStorageBufferAddresses);
        if (!_hasInt64) Emit(Op.Capability, Int64);
        _pushVariable = _nextId++;
        uint pushBlock = _nextId++;

        bool pendingDecorations = true;
        bool pendingGlobals = true;
        bool seenFirstLabel = false;
        bool pendingFunctionBases = false;
        ReadOnlySpan<uint> remaining = words[HeaderWords..];
        while (!remaining.IsEmpty)
        {
            ReadOnlySpan<uint> instruction = Next(ref remaining, out Op op);
            if (pendingDecorations && op is Op.Decorate or Op.MemberDecorate)
            {
                Emit(Op.Decorate, pushBlock, Block);
                Emit(Op.MemberDecorate, pushBlock, 0, Offset, 0);
                Emit(Op.MemberDecorate, pushBlock, 1, Offset, 8);
                pendingDecorations = false;
            }
            if (pendingFunctionBases && seenFirstLabel && op is not (Op.Variable or Op.Line or Op.NoLine))
            {
                EmitFunctionBases();
                pendingFunctionBases = false;
            }

            switch (op)
            {
                case Op.MemoryModel:
                    if (instruction[1] is not (0 or PhysicalStorageBuffer64)) throw Unsupported("Unsupported SPIR-V addressing model.");
                    Emit(Op.MemoryModel, PhysicalStorageBuffer64, instruction[2]);
                    continue;
                case Op.EntryPoint:
                    EmitEntryPoint(instruction);
                    continue;
                case Op.Decorate:
                    if (IsRawBuffer(instruction[1])) continue;
                    if (instruction[2] == DescriptorSet)
                    {
                        Emit(Op.Decorate, instruction[1], DescriptorSet, EmittedSet(instruction[1]));
                        continue;
                    }
                    break;
                case Op.Name when IsRawBuffer(instruction[1]):
                case Op.Variable when IsRawBuffer(instruction[2]):
                    continue;
                case Op.Function:
                    if (pendingGlobals)
                    {
                        EmitGlobals(pushBlock);
                        pendingGlobals = false;
                    }
                    PlanFunctionBases(remaining);
                    seenFirstLabel = false;
                    pendingFunctionBases = _functionBuffers.Count != 0;
                    break;
                case Op.Label:
                    seenFirstLabel = true;
                    break;
                case Op.FunctionEnd:
                    foreach (uint id in _functionBuffers) _ids[id].FunctionBase = 0;
                    _functionBuffers.Clear();
                    break;
                case Op.AccessChain or Op.InBoundsAccessChain when IsRawBuffer(instruction[3]):
                    _output.Add(instruction[0]);
                    _output.Add(_ids[_ids[instruction[1]].Type].PhysicalType);
                    _output.Add(instruction[2]);
                    _output.Add(_ids[instruction[3]].FunctionBase);
                    _output.AddRange(instruction[4..]);
                    continue;
                case Op.Load:
                    if (IsRawBuffer(instruction[3])) throw Unsupported("A root or buffer-table block cannot be loaded as a whole.");
                    uint alignment = _ids[instruction[3]].Alignment;
                    if (alignment == 0) break;
                    if (instruction.Length != 4) throw Unsupported("Unexpected SPIR-V memory operands on a root or buffer-table load.");
                    Emit(Op.Load, instruction[1], instruction[2], instruction[3], Aligned, alignment);
                    continue;
            }
            _output.AddRange(instruction);
        }

        _output[3] = _nextId;
        return MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_output)).ToArray();
    }

    private void EmitEntryPoint(ReadOnlySpan<uint> instruction)
    {
        int namesEnd = 3;
        while ((instruction[namesEnd++] & 0xff000000) != 0) { }
        int outputStart = _output.Count;
        _output.AddRange(instruction[..namesEnd]);
        foreach (uint id in instruction[namesEnd..])
        {
            if (!IsRawBuffer(id)) _output.Add(id);
        }
        _output.Add(_pushVariable);
        _output[outputStart] = ((uint)(_output.Count - outputStart) << 16) | (uint)Op.EntryPoint;
    }

    private void EmitGlobals(uint pushBlock)
    {
        if (_uint32 == 0) Emit(Op.TypeInt, _uint32 = _nextId++, 32, 0);
        if (_uint64 == 0) Emit(Op.TypeInt, _uint64 = _nextId++, 64, 0);
        foreach (uint pointee in _newPhysicalPointees) Emit(Op.TypePointer, _ids[pointee].PhysicalType, PhysicalStorageBuffer, pointee);

        uint pushBlockType = _nextId++;
        _pushAddressType = _nextId++;
        _rootMember = _nextId++;
        _tableMember = _nextId++;
        Emit(Op.TypeStruct, pushBlock, _uint64, _uint64);
        Emit(Op.TypePointer, pushBlockType, PushConstant, pushBlock);
        Emit(Op.TypePointer, _pushAddressType, PushConstant, _uint64);
        Emit(Op.Constant, _uint32, _rootMember, 0);
        Emit(Op.Constant, _uint32, _tableMember, 1);
        Emit(Op.Variable, pushBlockType, _pushVariable, PushConstant);
    }

    private void PlanFunctionBases(ReadOnlySpan<uint> remaining)
    {
        while (!remaining.IsEmpty)
        {
            ReadOnlySpan<uint> instruction = Next(ref remaining, out Op op);
            if (op == Op.FunctionEnd) break;
            if (op is not (Op.AccessChain or Op.InBoundsAccessChain)) continue;
            uint id = instruction[3];
            if (!IsRawBuffer(id) || _ids[id].FunctionBase != 0) continue;
            _ids[id].FunctionBase = _nextId++;
            _functionBuffers.Add(id);
        }
    }

    private void EmitFunctionBases()
    {
        // Slang can alias one raw root with several block types; each uses the same pushed address.
        uint root = 0;
        uint table = 0;
        foreach (uint id in _functionBuffers)
        {
            ref Id buffer = ref _ids[id];
            bool isRoot = buffer.Resource == Resource.Root;
            ref uint address = ref (isRoot ? ref root : ref table);
            if (address == 0)
            {
                uint pointer = _nextId++;
                address = _nextId++;
                Emit(Op.AccessChain, _pushAddressType, pointer, _pushVariable, isRoot ? _rootMember : _tableMember);
                Emit(Op.Load, _uint64, address, pointer);
            }
            Emit(Op.ConvertUToPtr, _ids[_ids[buffer.Type].Type].PhysicalType, buffer.FunctionBase, address);
        }
    }

    private ReadOnlySpan<uint> Next(ref ReadOnlySpan<uint> remaining, out Op op)
    {
        uint header = remaining[0];
        op = (Op)header;
        int count = (int)(header >> 16);
        if (count == 0 || count > remaining.Length) throw Unsupported("Invalid SPIR-V instruction length.");
        ReadOnlySpan<uint> instruction = remaining[..count];
        remaining = remaining[count..];
        return instruction;
    }

    private void Emit(Op op, params ReadOnlySpan<uint> operands)
    {
        _output.Add(((uint)(operands.Length + 1) << 16) | (uint)op);
        _output.AddRange(operands);
    }

    private ShaderCompilationException Unsupported(string reason) => new($"Vulkan Compact shader '{_entryPoint}': {reason}");
}
