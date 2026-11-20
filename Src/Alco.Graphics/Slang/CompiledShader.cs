// Pure-managed compiled shader artifact, GPU stage conversion, and binary serialization.

using System.Buffers.Binary;
using System.Text;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>One compiled programmable stage produced from a shader entry point.</summary>
public sealed class CompiledShaderStage
{
    /// <summary>The entry point function name in shader source.</summary>
    public required string SourceName { get; init; }

    /// <summary>The target-resolved entry point name inside <see cref="Code"/>.</summary>
    public required string CompiledEntryPoint { get; init; }

    /// <summary>The programmable pipeline stage.</summary>
    public required ShaderStageKind Stage { get; init; }

    /// <summary>The resolved threadgroup size for compute, mesh, and task stages; zero for other stages.</summary>
    public required Extent3D ThreadgroupSize { get; init; }

    /// <summary>The compiled target bytecode or Metal source bytes.</summary>
    public required ReadOnlyMemory<byte> Code { get; init; }

    /// <summary>Converts this compiled stage into a <see cref="Gpu.ShaderStage"/> descriptor.</summary>
    /// <returns>A shader stage referencing <see cref="Code"/>, its target entry name, and resolved threadgroup size.</returns>
    public ShaderStage ToGpuStage() => new()
    {
        Code = Code.Span,
        EntryPoint = CompiledEntryPoint,
        ThreadgroupSize = ThreadgroupSize.X != 0 ? ThreadgroupSize : new Extent3D(1, 1, 1),
    };
}

/// <summary>Immutable compiled shader program containing target code, dependencies, and pure-managed reflection.</summary>
public sealed class CompiledShader
{
    private const uint Magic = 0x48534741u; // "AGSH"
    private const ushort FormatVersion = 1;

    /// <summary>The compiled shader module name.</summary>
    public required string ModuleName { get; init; }

    /// <summary>The backend format <see cref="Stages"/> were compiled for.</summary>
    public required ShaderTarget Target { get; init; }

    /// <summary>Non-fatal compiler diagnostic warnings, or null when clean.</summary>
    public string? Diagnostics { get; init; }

    /// <summary>The compiled stages in entry-point order.</summary>
    public required IReadOnlyList<CompiledShaderStage> Stages { get; init; }

    /// <summary>Source and include file paths referenced by this shader, excluding built-in virtual headers.</summary>
    public required IReadOnlyList<string> DependencyPaths { get; init; }

    /// <summary>The lifetime-independent reflection snapshot for this program.</summary>
    public required ShaderReflection Reflection { get; init; }

    /// <summary>Finds the first compiled stage matching <paramref name="stage"/>.</summary>
    /// <param name="stage">The stage to look up.</param>
    /// <returns>The compiled stage, or null when absent.</returns>
    public CompiledShaderStage? FindStage(ShaderStageKind stage)
    {
        for (int i = 0; i < Stages.Count; i++)
        {
            if (Stages[i].Stage == stage)
                return Stages[i];
        }
        return null;
    }

    /// <summary>Gets the single compiled stage matching <paramref name="stage"/>.</summary>
    /// <param name="stage">The stage to retrieve.</param>
    /// <returns>The matching compiled stage.</returns>
    /// <exception cref="InvalidOperationException">No stage or multiple stages match <paramref name="stage"/>.</exception>
    public CompiledShaderStage GetStage(ShaderStageKind stage)
    {
        CompiledShaderStage? match = null;
        for (int i = 0; i < Stages.Count; i++)
        {
            if (Stages[i].Stage != stage)
                continue;
            if (match != null)
                throw new InvalidOperationException($"Shader '{ModuleName}' has more than one {stage} stage; select one by entry point name.");
            match = Stages[i];
        }
        return match ?? throw new InvalidOperationException($"Shader '{ModuleName}' does not contain a {stage} stage.");
    }

    /// <summary>Finds a compiled stage by its source entry-point function name.</summary>
    /// <param name="sourceEntryName">The entry-point function name in shader source.</param>
    /// <returns>The compiled stage, or null when absent.</returns>
    public CompiledShaderStage? FindStage(string sourceEntryName)
    {
        for (int i = 0; i < Stages.Count; i++)
        {
            if (Stages[i].SourceName == sourceEntryName)
                return Stages[i];
        }
        return null;
    }

    /// <summary>Gets a compiled stage by its source entry-point function name.</summary>
    /// <param name="sourceEntryName">The entry-point function name in shader source.</param>
    /// <returns>The compiled stage.</returns>
    /// <exception cref="InvalidOperationException">The entry point does not exist in this compiled shader.</exception>
    public CompiledShaderStage GetStage(string sourceEntryName) =>
        FindStage(sourceEntryName) ?? throw new InvalidOperationException($"Shader '{ModuleName}' does not contain entry point '{sourceEntryName}'.");

    /// <summary>Converts the unique <paramref name="stage"/> into a <see cref="Gpu.ShaderStage"/> descriptor.</summary>
    /// <param name="stage">The pipeline stage to convert.</param>
    /// <returns>The core <see cref="Gpu.ShaderStage"/> ready for pipeline creation.</returns>
    public ShaderStage GetGpuStage(ShaderStageKind stage) => GetStage(stage).ToGpuStage();

    /// <summary>Converts the stage named <paramref name="sourceEntryName"/> into a <see cref="Gpu.ShaderStage"/> descriptor.</summary>
    /// <param name="sourceEntryName">The entry-point function name in shader source.</param>
    /// <returns>The core <see cref="Gpu.ShaderStage"/> ready for pipeline creation.</returns>
    public ShaderStage GetGpuStage(string sourceEntryName) => GetStage(sourceEntryName).ToGpuStage();

    /// <summary>Attempts to convert a unique optional stage into a <see cref="Gpu.ShaderStage"/> descriptor.</summary>
    /// <param name="stage">The optional stage kind.</param>
    /// <param name="gpuStage">The converted stage when present; otherwise default.</param>
    /// <returns>True when a unique stage of <paramref name="stage"/> exists; false when absent.</returns>
    public bool TryGetGpuStage(ShaderStageKind stage, out ShaderStage gpuStage)
    {
        CompiledShaderStage? found = FindStage(stage);
        if (found == null)
        {
            gpuStage = default;
            return false;
        }
        for (int i = 0; i < Stages.Count; i++)
        {
            if (Stages[i] != found && Stages[i].Stage == stage)
                throw new InvalidOperationException($"Shader '{ModuleName}' has more than one {stage} stage; select one by entry point name.");
        }
        gpuStage = found.ToGpuStage();
        return true;
    }

    /// <summary>Serializes compiled stage code and full reflection metadata into a portable binary blob.</summary>
    /// <returns>The serialized binary shader artifact.</returns>
    public byte[] Serialize()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write((byte)Target);
        writer.Write((byte)0);
        writer.Write(ModuleName);
        WriteNullableString(writer, Diagnostics);

        writer.Write(DependencyPaths.Count);
        for (int i = 0; i < DependencyPaths.Count; i++)
            writer.Write(DependencyPaths[i]);

        writer.Write(Stages.Count);
        for (int i = 0; i < Stages.Count; i++)
        {
            CompiledShaderStage stage = Stages[i];
            writer.Write(stage.SourceName);
            writer.Write(stage.CompiledEntryPoint);
            writer.Write((uint)stage.Stage);
            writer.Write(stage.ThreadgroupSize.X);
            writer.Write(stage.ThreadgroupSize.Y);
            writer.Write(stage.ThreadgroupSize.Z);
            writer.Write(stage.Code.Length);
            writer.Write(stage.Code.Span);
        }

        WriteReflection(writer, Reflection);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Deserializes a binary shader artifact previously created by <see cref="Serialize"/>.</summary>
    /// <param name="data">The serialized artifact bytes.</param>
    /// <returns>The restored <see cref="CompiledShader"/> with intact code and reflection.</returns>
    /// <exception cref="InvalidDataException">The artifact header or payload is invalid.</exception>
    public static CompiledShader Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw new InvalidDataException("Invalid Alco.Graphics compiled shader artifact header.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2));
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported Alco.Graphics compiled shader artifact version {version}.");

        using MemoryStream stream = new(data.ToArray(), writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        reader.ReadUInt32(); // magic
        reader.ReadUInt16(); // version
        ShaderTarget target = (ShaderTarget)reader.ReadByte();
        if (!Enum.IsDefined(target))
            throw new InvalidDataException($"Unknown shader target {(byte)target} in serialized artifact.");
        reader.ReadByte(); // reserved
        string moduleName = reader.ReadString();
        string? diagnostics = ReadNullableString(reader);

        int depCount = reader.ReadInt32();
        string[] deps = depCount == 0 ? [] : new string[depCount];
        for (int i = 0; i < depCount; i++)
            deps[i] = reader.ReadString();

        int stageCount = reader.ReadInt32();
        CompiledShaderStage[] stages = stageCount == 0 ? [] : new CompiledShaderStage[stageCount];
        for (int i = 0; i < stageCount; i++)
        {
            string sourceName = reader.ReadString();
            string compiledEntry = reader.ReadString();
            ShaderStageKind stageKind = (ShaderStageKind)reader.ReadUInt32();
            uint tx = reader.ReadUInt32();
            uint ty = reader.ReadUInt32();
            uint tz = reader.ReadUInt32();
            int codeLen = reader.ReadInt32();
            byte[] code = reader.ReadBytes(codeLen);
            if (code.Length != codeLen)
                throw new InvalidDataException("Truncated shader bytecode in serialized artifact.");
            stages[i] = new CompiledShaderStage
            {
                SourceName = sourceName,
                CompiledEntryPoint = compiledEntry,
                Stage = stageKind,
                ThreadgroupSize = new Extent3D(tx, ty, tz),
                Code = code,
            };
        }

        ShaderReflection reflection = ReadReflection(reader);
        return new CompiledShader
        {
            ModuleName = moduleName,
            Target = target,
            Diagnostics = diagnostics,
            Stages = stages,
            DependencyPaths = deps,
            Reflection = reflection,
        };
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value != null);
        if (value != null)
            writer.Write(value);
    }

    private static string? ReadNullableString(BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadString() : null;

    private static void WriteAttributes(BinaryWriter writer, IReadOnlyList<ShaderAttribute> attributes)
    {
        writer.Write(attributes.Count);
        for (int i = 0; i < attributes.Count; i++)
        {
            ShaderAttribute attr = attributes[i];
            writer.Write(attr.Name);
            writer.Write(attr.Arguments.Count);
            for (int a = 0; a < attr.Arguments.Count; a++)
            {
                ShaderAttributeArgument arg = attr.Arguments[a];
                writer.Write((byte)arg.ScalarType);
                WriteNullableString(writer, arg.StringValue);
                writer.Write(arg.IntValue);
                writer.Write(arg.FloatValue);
                writer.Write(arg.DisplayValue);
            }
        }
    }

    private static ShaderAttribute[] ReadAttributes(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ShaderAttribute[] attributes = count == 0 ? [] : new ShaderAttribute[count];
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            int argCount = reader.ReadInt32();
            ShaderAttributeArgument[] args = argCount == 0 ? [] : new ShaderAttributeArgument[argCount];
            for (int a = 0; a < argCount; a++)
            {
                ShaderScalarType scalar = (ShaderScalarType)reader.ReadByte();
                string? str = ReadNullableString(reader);
                long intVal = reader.ReadInt64();
                float floatVal = reader.ReadSingle();
                string display = reader.ReadString();
                args[a] = new ShaderAttributeArgument
                {
                    ScalarType = scalar,
                    StringValue = str,
                    IntValue = intVal,
                    FloatValue = floatVal,
                    DisplayValue = display,
                };
            }
            attributes[i] = new ShaderAttribute { Name = name, Arguments = args };
        }
        return attributes;
    }

    private static void WriteFields(BinaryWriter writer, IReadOnlyList<ShaderFieldLayout> fields)
    {
        writer.Write(fields.Count);
        for (int i = 0; i < fields.Count; i++)
        {
            ShaderFieldLayout field = fields[i];
            writer.Write(field.Name);
            writer.Write(field.OffsetBytes);
            writer.Write(field.SizeBytes);
            writer.Write((byte)field.Semantic);
            WriteAttributes(writer, field.Attributes);
            WriteTypeLayout(writer, field.Type);
        }
    }

    private static ShaderFieldLayout[] ReadFields(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ShaderFieldLayout[] fields = count == 0 ? [] : new ShaderFieldLayout[count];
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            uint offset = reader.ReadUInt32();
            uint size = reader.ReadUInt32();
            ShaderFieldSemantic semantic = (ShaderFieldSemantic)reader.ReadByte();
            ShaderAttribute[] attrs = ReadAttributes(reader);
            ShaderTypeLayout type = ReadTypeLayout(reader);
            fields[i] = new ShaderFieldLayout
            {
                Name = name,
                OffsetBytes = offset,
                SizeBytes = size,
                Semantic = semantic,
                Attributes = attrs,
                Type = type,
            };
        }
        return fields;
    }

    private static void WriteTypeLayout(BinaryWriter writer, ShaderTypeLayout type)
    {
        writer.Write((byte)type.Kind);
        WriteNullableString(writer, type.TypeName);
        writer.Write(type.SizeBytes);
        writer.Write(type.AlignmentBytes);
        writer.Write(type.StrideBytes);
        writer.Write(type.ElementCount);
        writer.Write((byte)type.ScalarType);
        writer.Write(type.VectorLength);
        writer.Write(type.MatrixRows);
        writer.Write(type.MatrixColumns);
        writer.Write((byte)type.MatrixLayout);
        writer.Write((byte)type.Semantic);
        WriteAttributes(writer, type.Attributes);
        WriteFields(writer, type.Fields);
        writer.Write(type.ElementType != null);
        if (type.ElementType != null)
            WriteTypeLayout(writer, type.ElementType);
    }

    private static ShaderTypeLayout ReadTypeLayout(BinaryReader reader)
    {
        ShaderTypeKind kind = (ShaderTypeKind)reader.ReadByte();
        string? typeName = ReadNullableString(reader);
        uint size = reader.ReadUInt32();
        uint alignment = reader.ReadUInt32();
        uint stride = reader.ReadUInt32();
        uint elementCount = reader.ReadUInt32();
        ShaderScalarType scalar = (ShaderScalarType)reader.ReadByte();
        byte vectorLen = reader.ReadByte();
        byte matrixRows = reader.ReadByte();
        byte matrixCols = reader.ReadByte();
        ShaderMatrixLayout matrixLayout = (ShaderMatrixLayout)reader.ReadByte();
        ShaderFieldSemantic semantic = (ShaderFieldSemantic)reader.ReadByte();
        ShaderAttribute[] attrs = ReadAttributes(reader);
        ShaderFieldLayout[] fields = ReadFields(reader);
        ShaderTypeLayout? elementType = reader.ReadBoolean() ? ReadTypeLayout(reader) : null;
        return new ShaderTypeLayout
        {
            Kind = kind,
            TypeName = typeName,
            SizeBytes = size,
            AlignmentBytes = alignment,
            StrideBytes = stride,
            ElementCount = elementCount,
            ElementType = elementType,
            ScalarType = scalar,
            VectorLength = vectorLen,
            MatrixRows = matrixRows,
            MatrixColumns = matrixCols,
            MatrixLayout = matrixLayout,
            Semantic = semantic,
            Fields = fields,
            Attributes = attrs,
        };
    }

    private static void WriteStageVariables(BinaryWriter writer, IReadOnlyList<ShaderStageVariable> vars)
    {
        writer.Write(vars.Count);
        for (int i = 0; i < vars.Count; i++)
        {
            ShaderStageVariable v = vars[i];
            writer.Write(v.Name);
            WriteNullableString(writer, v.SemanticName);
            writer.Write(v.SemanticIndex);
            WriteTypeLayout(writer, v.Type);
        }
    }

    private static ShaderStageVariable[] ReadStageVariables(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        ShaderStageVariable[] vars = count == 0 ? [] : new ShaderStageVariable[count];
        for (int i = 0; i < count; i++)
        {
            vars[i] = new ShaderStageVariable
            {
                Name = reader.ReadString(),
                SemanticName = ReadNullableString(reader),
                SemanticIndex = reader.ReadUInt32(),
                Type = ReadTypeLayout(reader),
            };
        }
        return vars;
    }

    private static void WriteReflection(BinaryWriter writer, ShaderReflection reflection)
    {
        writer.Write(reflection.Root != null);
        if (reflection.Root != null)
        {
            writer.Write(reflection.Root.TypeName);
            writer.Write(reflection.Root.SizeBytes);
            writer.Write(reflection.Root.AlignmentBytes);
            WriteFields(writer, reflection.Root.Fields);
        }

        writer.Write(reflection.EntryPoints.Count);
        for (int i = 0; i < reflection.EntryPoints.Count; i++)
        {
            ShaderEntryPointReflection ep = reflection.EntryPoints[i];
            writer.Write(ep.SourceName);
            writer.Write(ep.CompiledEntryPoint);
            writer.Write((uint)ep.Stage);
            writer.Write(ep.ThreadgroupSize.X);
            writer.Write(ep.ThreadgroupSize.Y);
            writer.Write(ep.ThreadgroupSize.Z);
            WriteAttributes(writer, ep.Attributes);
            WriteStageVariables(writer, ep.Inputs);
            WriteStageVariables(writer, ep.Outputs);
            writer.Write(ep.GenericParameters.Count);
            for (int g = 0; g < ep.GenericParameters.Count; g++)
            {
                ShaderGenericParam gp = ep.GenericParameters[g];
                writer.Write(gp.Name);
                writer.Write(gp.IsTypeParameter);
                WriteNullableString(writer, gp.ConstraintTypeName);
                writer.Write((byte)gp.ValueScalarType);
            }
        }

        writer.Write(reflection.Types.Count);
        for (int i = 0; i < reflection.Types.Count; i++)
            WriteTypeLayout(writer, reflection.Types[i]);

        writer.Write(reflection.Globals.Count);
        for (int i = 0; i < reflection.Globals.Count; i++)
        {
            ShaderParameterInfo global = reflection.Globals[i];
            writer.Write(global.Name);
            writer.Write(global.BindingIndex);
            writer.Write(global.BindingSpace);
            WriteTypeLayout(writer, global.Type);
        }
    }

    private static ShaderReflection ReadReflection(BinaryReader reader)
    {
        ShaderRootLayout? root = null;
        if (reader.ReadBoolean())
        {
            root = new ShaderRootLayout
            {
                TypeName = reader.ReadString(),
                SizeBytes = reader.ReadUInt32(),
                AlignmentBytes = reader.ReadUInt32(),
                Fields = ReadFields(reader),
            };
        }

        int epCount = reader.ReadInt32();
        ShaderEntryPointReflection[] entries = epCount == 0 ? [] : new ShaderEntryPointReflection[epCount];
        for (int i = 0; i < epCount; i++)
        {
            string sourceName = reader.ReadString();
            string compiledEntry = reader.ReadString();
            ShaderStageKind stage = (ShaderStageKind)reader.ReadUInt32();
            uint tx = reader.ReadUInt32();
            uint ty = reader.ReadUInt32();
            uint tz = reader.ReadUInt32();
            ShaderAttribute[] attrs = ReadAttributes(reader);
            ShaderStageVariable[] inputs = ReadStageVariables(reader);
            ShaderStageVariable[] outputs = ReadStageVariables(reader);
            int gpCount = reader.ReadInt32();
            ShaderGenericParam[] generics = gpCount == 0 ? [] : new ShaderGenericParam[gpCount];
            for (int g = 0; g < gpCount; g++)
            {
                generics[g] = new ShaderGenericParam
                {
                    Name = reader.ReadString(),
                    IsTypeParameter = reader.ReadBoolean(),
                    ConstraintTypeName = ReadNullableString(reader),
                    ValueScalarType = (ShaderScalarType)reader.ReadByte(),
                };
            }
            entries[i] = new ShaderEntryPointReflection
            {
                SourceName = sourceName,
                CompiledEntryPoint = compiledEntry,
                Stage = stage,
                ThreadgroupSize = new Extent3D(tx, ty, tz),
                Attributes = attrs,
                Inputs = inputs,
                Outputs = outputs,
                GenericParameters = generics,
            };
        }

        int typeCount = reader.ReadInt32();
        ShaderTypeLayout[] types = typeCount == 0 ? [] : new ShaderTypeLayout[typeCount];
        for (int i = 0; i < typeCount; i++)
            types[i] = ReadTypeLayout(reader);

        int globalCount = reader.ReadInt32();
        ShaderParameterInfo[] globals = globalCount == 0 ? [] : new ShaderParameterInfo[globalCount];
        for (int i = 0; i < globalCount; i++)
        {
            globals[i] = new ShaderParameterInfo
            {
                Name = reader.ReadString(),
                BindingIndex = reader.ReadUInt32(),
                BindingSpace = reader.ReadUInt32(),
                Type = ReadTypeLayout(reader),
            };
        }

        return new ShaderReflection
        {
            Root = root,
            EntryPoints = entries,
            Types = types,
            Globals = globals,
        };
    }
}
