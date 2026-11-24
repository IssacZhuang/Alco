// Internal Slang reflection reader converting native layouts into immutable Gpu.Shaders reflection snapshots.

using System.Globalization;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics.Slang;

internal readonly record struct SlangGenericParameter(IntPtr Variable, IntPtr Name, bool IsType);

internal static unsafe class SlangLayoutReader
{
    private const int MaxTypeDepth = 32;

    private static ShaderRootLayout? ReadProgramRootLayout(
        IntPtr reflection, IntPtr moduleDecl, int entryCount, ShaderTarget target, string programName)
    {
        using SlangPinnedUtf8 name = new("GPURootArguments");
        IntPtr type = SlangNative.spReflection_FindTypeByName(reflection, name.Pointer);
        IntPtr generic;
        if (type != IntPtr.Zero)
        {
            generic = SlangNative.spReflectionType_GetGenericContainer(type);
            // A bare generic declaration is not a layout; a typedef to a concrete specialization is.
            if (generic == IntPtr.Zero || type != SlangNative.spReflection_getTypeFromDecl(SlangNative.spReflectionGeneric_GetInnerDecl(generic)))
                return ReadRootLayoutFromType(reflection, type);
        }
        else
        {
            (type, generic) = FindRootStruct(moduleDecl, 0);
            if (type == IntPtr.Zero)
                return null;
            if (generic == IntPtr.Zero)
                return ReadRootLayoutFromType(reflection, type);
        }

        IntPtr concrete = ReadGenericRootType(reflection, type, generic, entryCount, target, programName);
        return ReadRootLayoutFromType(reflection, concrete);
    }

    internal static ShaderReflection ReadProgramReflection(
        IntPtr reflection,
        IntPtr moduleDecl,
        SlangCodeTarget target,
        IReadOnlyList<SlangEntryPointInfo> compiledEntries,
        string programName)
    {
        List<ShaderParameterInfo> globals = ReadGlobalParameters(reflection);
        ShaderRootLayout? root = ReadProgramRootLayout(
            reflection, moduleDecl, compiledEntries.Count, SlangCompileSession.ToPublicTarget(target), programName);
        List<ShaderTypeLayout> types = ReadModuleTypes(reflection, moduleDecl);
        ShaderEntryPointReflection[] entries = ReadEntryPointReflections(reflection, target, compiledEntries);
        return new ShaderReflection
        {
            Root = root,
            EntryPoints = entries,
            Types = types,
            Globals = globals,
        };
    }

    internal static List<ShaderParameterInfo> ReadGlobalParameters(IntPtr reflection)
    {
        uint count = SlangNative.spReflection_GetParameterCount(reflection);
        List<ShaderParameterInfo> parameters = new((int)count);
        for (uint index = 0; index < count; index++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(reflection, index);
            if (parameter == IntPtr.Zero)
                continue;
            IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(parameter);
            string? name = variable != IntPtr.Zero ? SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable)) : null;
            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            parameters.Add(new ShaderParameterInfo
            {
                Name = name ?? string.Empty,
                BindingIndex = SlangNative.spReflectionParameter_GetBindingIndex(parameter),
                BindingSpace = SlangNative.spReflectionParameter_GetBindingSpace(parameter),
                Type = ReadTypeLayout(typeLayout),
            });
        }
        return parameters;
    }

    private static IntPtr ReadGenericRootType(IntPtr reflection, IntPtr type, IntPtr generic, int entryCount, ShaderTarget target, string programName)
    {
        SlangGenericParameter[] rootParams = ReadOrderedGenericParameters(generic);
        if (rootParams.Length == 0)
            return IntPtr.Zero;

        int[] slots = new int[rootParams.Length];
        SlangGenericArgKind[] kinds = new SlangGenericArgKind[rootParams.Length];
        SlangGenericArg[] args = new SlangGenericArg[rootParams.Length];
        IntPtr concrete = IntPtr.Zero;
        for (int i = 0; i < entryCount; i++)
        {
            IntPtr entryGeneric = GetEntryPointGeneric(reflection, (nuint)i);
            SlangGenericParameter[] entryParams = ReadOrderedGenericParameters(entryGeneric);
            if (entryParams.Length < rootParams.Length || !BindRootParameters(rootParams, entryParams, slots) ||
                !ReadRootArguments(entryGeneric, entryParams, slots, kinds, args))
                continue;

            IntPtr specialized;
            IntPtr diagnostics = IntPtr.Zero;
            fixed (SlangGenericArgKind* kindPtr = kinds)
            fixed (SlangGenericArg* argPtr = args)
                specialized = SlangNative.spReflection_specializeGeneric(reflection, generic, args.Length, kindPtr, argPtr, &diagnostics);
            if (diagnostics != IntPtr.Zero)
                new SlangBlob(diagnostics).Release();
            if (specialized == IntPtr.Zero)
                continue;

            IntPtr candidate = SlangNative.spReflectionType_applySpecializations(type, specialized);
            if (candidate == IntPtr.Zero || candidate == type)
                continue;
            if (concrete != IntPtr.Zero && concrete != candidate)
            {
                string? name = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(type));
                throw new ShaderCompilationException(
                    $"Shader '{programName}': selected entry points specialize root '{name}' differently.", target, programName);
            }
            concrete = candidate;
        }
        return concrete;
    }

    private static bool BindRootParameters(ReadOnlySpan<SlangGenericParameter> root, ReadOnlySpan<SlangGenericParameter> entry, Span<int> slots)
    {
        // Slang does not reflect body-local root loads. First match parameters by name and kind.
        slots.Fill(-1);
        for (int r = 0; r < root.Length; r++)
        {
            for (int e = 0; e < entry.Length; e++)
            {
                if (root[r].IsType == entry[e].IsType && root[r].Name == entry[e].Name)
                {
                    slots[r] = e;
                    break;
                }
            }
        }

        // Unmatched parameters can only be paired by order when their kind has equal arity.
        for (int r = 0; r < root.Length; r++)
        {
            if (slots[r] >= 0)
                continue;
            bool isType = root[r].IsType;
            int remainingRoots = 0;
            for (int other = r; other < root.Length; other++)
                if (slots[other] < 0 && root[other].IsType == isType)
                    remainingRoots++;
            int remainingEntries = 0;
            int first = -1;
            for (int e = 0; e < entry.Length; e++)
            {
                if (entry[e].IsType != isType || slots.Contains(e))
                    continue;
                if (first < 0)
                    first = e;
                remainingEntries++;
            }
            if (remainingEntries != remainingRoots)
                return false;
            slots[r] = first;
        }
        return true;
    }

    private static bool ReadRootArguments(
        IntPtr generic, SlangGenericParameter[] parameters, int[] slots, SlangGenericArgKind[] kinds, SlangGenericArg[] args)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            SlangGenericParameter parameter = parameters[slots[i]];
            if (parameter.IsType)
            {
                IntPtr type = SlangNative.spReflectionGeneric_GetConcreteType(generic, parameter.Variable);
                // Slang's generic specialization API requires a declaration-backed type argument.
                if (type == IntPtr.Zero || SlangNative.spReflectionType_GetName(type) == IntPtr.Zero)
                    return false;
                kinds[i] = SlangGenericArgKind.Type;
                args[i] = new SlangGenericArg { Type = type };
            }
            else
            {
                long value = SlangNative.spReflectionGeneric_GetConcreteIntVal(generic, parameter.Variable);
                IntPtr type = SlangNative.spReflectionVariable_GetType(parameter.Variable);
                switch ((ShaderScalarType)SlangNative.spReflectionType_GetScalarType(type))
                {
                    case ShaderScalarType.Bool:
                        kinds[i] = SlangGenericArgKind.Bool;
                        args[i] = new SlangGenericArg { Bool = (byte)(value != 0 ? 1 : 0) };
                        break;
                    case ShaderScalarType.Int8 or ShaderScalarType.UInt8 or ShaderScalarType.Int16 or ShaderScalarType.UInt16 or
                        ShaderScalarType.Int32 or ShaderScalarType.UInt32 or ShaderScalarType.Int64 or ShaderScalarType.UInt64:
                        kinds[i] = SlangGenericArgKind.Int;
                        args[i] = new SlangGenericArg { Int = value };
                        break;
                    default:
                        return false;
                }
            }
        }
        return true;
    }

    private static ShaderRootLayout? ReadRootLayoutFromType(IntPtr reflection, IntPtr type)
    {
        if (type == IntPtr.Zero)
            return null;
        IntPtr typeLayoutPtr = SlangNative.spReflection_GetTypeLayout(reflection, type, SlangNative.SLANG_LAYOUT_RULES_STRUCTURED_BUFFER);
        if (typeLayoutPtr == IntPtr.Zero ||
            SlangNative.spReflectionTypeLayout_GetSize(typeLayoutPtr, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM) >= nuint.MaxValue - 1)
            return null;
        ShaderTypeLayout rootType = ReadTypeLayout(typeLayoutPtr);
        if (rootType.Kind != ShaderTypeKind.Struct)
            return null;
        return new ShaderRootLayout
        {
            TypeName = rootType.TypeName ?? "GPURootArguments",
            SizeBytes = rootType.SizeBytes,
            AlignmentBytes = rootType.AlignmentBytes,
            Fields = rootType.Fields,
        };
    }

    private static (IntPtr Type, IntPtr Generic) FindRootStruct(IntPtr parentDecl, int depth)
    {
        if (parentDecl == IntPtr.Zero || depth > 16)
            return default;
        uint count = SlangNative.spReflectionDecl_getChildrenCount(parentDecl);
        for (uint index = 0; index < count; index++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(parentDecl, index);
            if (child == IntPtr.Zero)
                continue;
            int kind = SlangNative.spReflectionDecl_getKind(child);
            if (kind == SlangNative.SLANG_DECL_KIND_NAMESPACE || kind == SlangNative.SLANG_DECL_KIND_MODULE)
            {
                (IntPtr nestedType, IntPtr nestedGeneric) = FindRootStruct(child, depth + 1);
                if (nestedType != IntPtr.Zero)
                    return (nestedType, nestedGeneric);
                continue;
            }
            if (kind == SlangNative.SLANG_DECL_KIND_STRUCT)
            {
                IntPtr structType = SlangNative.spReflection_getTypeFromDecl(child);
                if (structType != IntPtr.Zero && HasTypeAttribute(structType, "GPURoot"))
                    return (structType, IntPtr.Zero);
                continue;
            }
            if (kind != SlangNative.SLANG_DECL_KIND_GENERIC)
                continue;
            IntPtr generic = SlangNative.spReflectionDecl_castToGeneric(child);
            if (generic == IntPtr.Zero)
                continue;
            IntPtr inner = SlangNative.spReflectionGeneric_GetInnerDecl(generic);
            if (inner == IntPtr.Zero || SlangNative.spReflectionDecl_getKind(inner) != SlangNative.SLANG_DECL_KIND_STRUCT)
                continue;
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionDecl_getName(inner));
            IntPtr innerType = SlangNative.spReflection_getTypeFromDecl(inner);
            if (innerType != IntPtr.Zero && (name == "GPURootArguments" || HasTypeAttribute(innerType, "GPURoot")))
                return (innerType, generic);
        }
        return default;
    }

    private static bool HasTypeAttribute(IntPtr type, string attributeName)
    {
        uint count = SlangNative.spReflectionType_GetUserAttributeCount(type);
        for (uint i = 0; i < count; i++)
        {
            IntPtr attr = SlangNative.spReflectionType_GetUserAttribute(type, i);
            if (attr == IntPtr.Zero)
                continue;
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionUserAttribute_GetName(attr));
            if (name == attributeName)
                return true;
        }
        return false;
    }

    internal static List<ShaderTypeLayout> ReadModuleTypes(IntPtr reflection, IntPtr moduleDecl)
    {
        List<ShaderTypeLayout> types = [];
        if (moduleDecl == IntPtr.Zero)
            return types;
        HashSet<string> seen = new(StringComparer.Ordinal);
        CollectDeclTypes(reflection, moduleDecl, types, seen, 0);
        return types;
    }

    private static void CollectDeclTypes(IntPtr reflection, IntPtr parentDecl, List<ShaderTypeLayout> types, HashSet<string> seen, int depth)
    {
        if (depth > 16)
            return;
        uint count = SlangNative.spReflectionDecl_getChildrenCount(parentDecl);
        for (uint index = 0; index < count; index++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(parentDecl, index);
            if (child == IntPtr.Zero)
                continue;
            int kind = SlangNative.spReflectionDecl_getKind(child);
            if (kind == SlangNative.SLANG_DECL_KIND_NAMESPACE || kind == SlangNative.SLANG_DECL_KIND_MODULE)
            {
                CollectDeclTypes(reflection, child, types, seen, depth + 1);
                continue;
            }
            if (kind != SlangNative.SLANG_DECL_KIND_STRUCT && kind != SlangNative.SLANG_DECL_KIND_ENUM)
                continue;
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionDecl_getName(child));
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
                continue;
            IntPtr type = SlangNative.spReflection_getTypeFromDecl(child);
            if (type == IntPtr.Zero)
                continue;
            IntPtr typeLayout = SlangNative.spReflection_GetTypeLayout(reflection, type, SlangNative.SLANG_LAYOUT_RULES_STRUCTURED_BUFFER);
            if (typeLayout != IntPtr.Zero)
                types.Add(ReadTypeLayout(typeLayout));
        }
    }

    internal static SlangEntryPointInfo[] ReadEntryPoints(
        IntPtr reflection, string moduleName, int entryCount, IReadOnlyList<SlangEntryPointRequest>? requests)
    {
        int count = checked((int)SlangNative.spReflection_getEntryPointCount(reflection));
        if (count != entryCount)
            throw new ShaderCompilationException($"Shader '{moduleName}' reflected {count} entry points but {entryCount} were composed.");

        SlangEntryPointInfo[] entries = new SlangEntryPointInfo[count];
        for (int index = 0; index < count; index++)
        {
            SlangEntryPointInfo? entry = ReadEntryPoint(reflection, index, readThreadgroupSize: true, out int nativeStage);
            if (requests != null && entry?.Stage != requests[index].Stage)
            {
                SlangEntryPointRequest request = requests[index];
                string actualStage = entry?.Stage.ToString() ?? $"unsupported native stage {nativeStage}";
                throw new ShaderCompilationException(
                    $"Shader entry point '{request.Name}' in module '{moduleName}' has stage {actualStage}, not the requested stage {request.Stage}.");
            }
            entries[index] = entry ?? throw new NotSupportedException($"Unknown slang stage id {nativeStage}.");
        }
        return entries;
    }

    internal static SlangEntryPointInfo? ReadEntryPoint(IntPtr reflection, int index, bool readThreadgroupSize, out int nativeStage)
    {
        IntPtr entryPoint = SlangNative.spReflection_getEntryPointByIndex(reflection, (nuint)index);
        string? name = entryPoint == IntPtr.Zero ? null : SlangNative.StringFromPtr(SlangNative.spReflectionEntryPoint_getName(entryPoint));
        if (name == null)
            throw new InvalidOperationException($"Shader reflection failed to provide entry point {index}.");
        nativeStage = SlangNative.spReflectionEntryPoint_getStage(entryPoint);
        SlangShaderStage? stage = SlangCompileSession.MapSlangStage(nativeStage);
        if (!stage.HasValue)
            return null;

        (uint X, uint Y, uint Z) threadgroupSize = default;
        if (readThreadgroupSize && stage is SlangShaderStage.Compute or SlangShaderStage.Task or SlangShaderStage.Mesh)
        {
            nuint* size = stackalloc nuint[3] { 0, 0, 0 };
            SlangNative.spReflectionEntryPoint_getComputeThreadGroupSize(entryPoint, 3, size);
            threadgroupSize = ((uint)size[0], (uint)size[1], (uint)size[2]);
        }
        return new SlangEntryPointInfo(name, stage.Value) { ThreadgroupSize = threadgroupSize };
    }

    private static ShaderEntryPointReflection[] ReadEntryPointReflections(
        IntPtr reflection, SlangCodeTarget target, IReadOnlyList<SlangEntryPointInfo> compiledEntries)
    {
        int count = compiledEntries.Count;
        ShaderEntryPointReflection[] result = new ShaderEntryPointReflection[count];
        for (int index = 0; index < count; index++)
        {
            SlangEntryPointInfo info = compiledEntries[index];
            IntPtr ep = SlangNative.spReflection_getEntryPointByIndex(reflection, (nuint)index);
            ShaderAttribute[] attributes = [];
            List<ShaderStageVariable> inputs = [];
            List<ShaderStageVariable> outputs = [];
            List<ShaderGenericParam> generics = [];
            if (ep != IntPtr.Zero)
            {
                IntPtr func = SlangNative.spReflectionEntryPoint_getFunction(ep);
                if (func != IntPtr.Zero)
                {
                    attributes = ReadFunctionAttributes(func);
                    IntPtr generic = SlangNative.spReflectionFunction_GetGenericContainer(func);
                    if (generic != IntPtr.Zero)
                        ReadGenericParams(generic, generics);
                }

                uint paramCount = SlangNative.spReflectionEntryPoint_getParameterCount(ep);
                for (uint p = 0; p < paramCount; p++)
                {
                    IntPtr varLayout = SlangNative.spReflectionEntryPoint_getParameterByIndex(ep, p);
                    if (varLayout != IntPtr.Zero)
                        CollectStageVariables(varLayout, inputs);
                }

                IntPtr resultLayout = SlangNative.spReflectionEntryPoint_getResultVarLayout(ep);
                if (resultLayout != IntPtr.Zero)
                    CollectStageVariables(resultLayout, outputs);
            }

            (uint x, uint y, uint z) = info.ThreadgroupSize;
            result[index] = new ShaderEntryPointReflection
            {
                SourceName = info.Name,
                CompiledEntryPoint = target.EntryPointName(info.Name),
                Stage = ToPublicStage(info.Stage),
                ThreadgroupSize = new Extent3D(x, y, z),
                Attributes = attributes,
                Inputs = inputs,
                Outputs = outputs,
                GenericParameters = generics,
            };
        }
        return result;
    }

    private static void CollectStageVariables(IntPtr varLayout, List<ShaderStageVariable> destination)
    {
        IntPtr typeLayoutPtr = SlangNative.spReflectionVariableLayout_GetTypeLayout(varLayout);
        if (typeLayoutPtr == IntPtr.Zero)
            return;
        ShaderTypeLayout typeLayout = ReadTypeLayout(typeLayoutPtr);
        if (typeLayout.Kind == ShaderTypeKind.Scalar && typeLayout.ScalarType == ShaderScalarType.Void)
            return;

        IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(varLayout);
        string varName = (variable != IntPtr.Zero ? SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable)) : null) ?? string.Empty;
        string? semantic = SlangNative.StringFromPtr(SlangNative.spReflectionVariableLayout_GetSemanticName(varLayout));
        uint semanticIndex = (uint)SlangNative.spReflectionVariableLayout_GetSemanticIndex(varLayout);

        if (typeLayout.Kind == ShaderTypeKind.Struct && string.IsNullOrEmpty(semantic))
        {
            uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(typeLayoutPtr);
            for (uint i = 0; i < fieldCount; i++)
            {
                IntPtr field = SlangNative.spReflectionTypeLayout_GetFieldByIndex(typeLayoutPtr, i);
                if (field != IntPtr.Zero)
                    CollectStageVariables(field, destination);
            }
            return;
        }

        destination.Add(new ShaderStageVariable
        {
            Name = varName,
            SemanticName = semantic,
            SemanticIndex = semanticIndex,
            Type = typeLayout,
        });
    }

    internal static IntPtr GetEntryPointGeneric(IntPtr reflection, nuint index)
    {
        IntPtr entry = SlangNative.spReflection_getEntryPointByIndex(reflection, index);
        IntPtr function = SlangNative.spReflectionEntryPoint_getFunction(entry);
        return function == IntPtr.Zero ? IntPtr.Zero : SlangNative.spReflectionFunction_GetGenericContainer(function);
    }

    internal static SlangGenericParameter[] ReadOrderedGenericParameters(IntPtr generic)
    {
        if (generic == IntPtr.Zero)
            return [];
        uint typeCount = SlangNative.spReflectionGeneric_GetTypeParameterCount(generic);
        uint valueCount = SlangNative.spReflectionGeneric_GetValueParameterCount(generic);
        SlangGenericParameter[] result = new SlangGenericParameter[typeCount + valueCount];

        // The separate native type and value lists preserve order only within each kind.
        IntPtr declaration = SlangNative.spReflectionGeneric_asDecl(generic);
        uint childCount = SlangNative.spReflectionDecl_getChildrenCount(declaration);
        uint t = 0, v = 0;
        int written = 0;
        IntPtr type = IntPtr.Zero, value = IntPtr.Zero;
        for (uint i = 0; i < childCount && written < result.Length; i++)
        {
            if (type == IntPtr.Zero && t < typeCount)
                type = SlangNative.spReflectionGeneric_GetTypeParameter(generic, t);
            if (value == IntPtr.Zero && v < valueCount)
                value = SlangNative.spReflectionGeneric_GetValueParameter(generic, v);

            IntPtr name = SlangNative.spReflectionDecl_getName(SlangNative.spReflectionDecl_getChild(declaration, i));
            if (type != IntPtr.Zero && name == SlangNative.spReflectionVariable_GetName(type))
            {
                result[written++] = new SlangGenericParameter(type, name, true);
                type = IntPtr.Zero;
                t++;
            }
            else if (value != IntPtr.Zero && name == SlangNative.spReflectionVariable_GetName(value))
            {
                result[written++] = new SlangGenericParameter(value, name, false);
                value = IntPtr.Zero;
                v++;
            }
        }
        return written == result.Length ? result : [];
    }

    internal static ShaderGenericParam[] ReadEntryPointGenericParameters(IntPtr layout)
    {
        IntPtr generic = GetEntryPointGeneric(layout, 0);
        if (generic == IntPtr.Zero)
            return [];
        List<ShaderGenericParam> generics = [];
        ReadGenericParams(generic, generics);
        return generics.Count == 0 ? [] : generics.ToArray();
    }

    private static void ReadGenericParams(IntPtr generic, List<ShaderGenericParam> destination)
    {
        uint typeParamCount = SlangNative.spReflectionGeneric_GetTypeParameterCount(generic);
        for (uint i = 0; i < typeParamCount; i++)
        {
            IntPtr tp = SlangNative.spReflectionGeneric_GetTypeParameter(generic, i);
            if (tp == IntPtr.Zero)
                continue;
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(tp));
            string? constraintName = null;
            uint constraintCount = SlangNative.spReflectionGeneric_GetTypeParameterConstraintCount(generic, tp);
            if (constraintCount > 0)
            {
                IntPtr constraintType = SlangNative.spReflectionGeneric_GetTypeParameterConstraintType(generic, tp, 0);
                if (constraintType != IntPtr.Zero)
                    constraintName = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(constraintType));
            }
            destination.Add(new ShaderGenericParam
            {
                Name = name ?? string.Empty,
                IsTypeParameter = true,
                ConstraintTypeName = constraintName,
            });
        }

        uint valueParamCount = SlangNative.spReflectionGeneric_GetValueParameterCount(generic);
        for (uint i = 0; i < valueParamCount; i++)
        {
            IntPtr vp = SlangNative.spReflectionGeneric_GetValueParameter(generic, i);
            if (vp == IntPtr.Zero)
                continue;
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(vp));
            IntPtr type = SlangNative.spReflectionVariable_GetType(vp);
            ShaderScalarType scalar = type != IntPtr.Zero
                ? (ShaderScalarType)SlangNative.spReflectionType_GetScalarType(type)
                : ShaderScalarType.None;
            destination.Add(new ShaderGenericParam
            {
                Name = name ?? string.Empty,
                IsTypeParameter = false,
                ValueScalarType = scalar,
            });
        }
    }

    internal static ShaderTypeLayout ReadTypeLayout(IntPtr typeLayout, int depth = 0)
    {
        if (typeLayout == IntPtr.Zero || depth > MaxTypeDepth)
        {
            return new ShaderTypeLayout
            {
                Kind = ShaderTypeKind.None,
                SizeBytes = 0,
            };
        }

        ShaderTypeKind kind = (ShaderTypeKind)SlangNative.spReflectionTypeLayout_getKind(typeLayout);
        uint size = (uint)SlangNative.spReflectionTypeLayout_GetSize(typeLayout, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
        uint alignment = (uint)SlangNative.spReflectionTypeLayout_getAlignment(typeLayout, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
        IntPtr type = SlangNative.spReflectionTypeLayout_GetType(typeLayout);
        string? typeName = type != IntPtr.Zero ? SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(type)) : null;

        ShaderScalarType scalarType = ShaderScalarType.None;
        byte vectorLength = 0;
        byte matrixRows = 0;
        byte matrixColumns = 0;
        ShaderMatrixLayout matrixLayout = ShaderMatrixLayout.None;
        uint strideBytes = 0;
        uint elementCount = 0;
        ShaderTypeLayout? elementType = null;
        ShaderFieldLayout[] fields = [];
        ShaderAttribute[] attributes = type != IntPtr.Zero ? ReadTypeAttributes(type) : [];

        if (type != IntPtr.Zero && kind is ShaderTypeKind.Scalar or ShaderTypeKind.Vector or ShaderTypeKind.Matrix or ShaderTypeKind.Enum)
            scalarType = (ShaderScalarType)SlangNative.spReflectionType_GetScalarType(type);

        if (kind == ShaderTypeKind.Vector && type != IntPtr.Zero)
            vectorLength = (byte)SlangNative.spReflectionType_GetElementCount(type);
        else if (kind == ShaderTypeKind.Matrix && type != IntPtr.Zero)
        {
            matrixRows = (byte)SlangNative.spReflectionType_GetRowCount(type);
            matrixColumns = (byte)SlangNative.spReflectionType_GetColumnCount(type);
            matrixLayout = (ShaderMatrixLayout)SlangNative.spReflectionTypeLayout_GetMatrixLayoutMode(typeLayout);
        }
        else if (kind == ShaderTypeKind.Struct)
        {
            uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(typeLayout);
            List<ShaderFieldLayout> fieldList = new((int)fieldCount);
            for (uint index = 0; index < fieldCount; index++)
            {
                IntPtr field = SlangNative.spReflectionTypeLayout_GetFieldByIndex(typeLayout, index);
                if (field == IntPtr.Zero)
                    continue;
                IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(field);
                string? fieldName = variable != IntPtr.Zero ? SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable)) : null;
                ShaderTypeLayout fieldType = ReadTypeLayout(SlangNative.spReflectionVariableLayout_GetTypeLayout(field), depth + 1);
                fieldList.Add(new ShaderFieldLayout
                {
                    Name = fieldName ?? string.Empty,
                    OffsetBytes = (uint)SlangNative.spReflectionVariableLayout_GetOffset(field, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM),
                    SizeBytes = fieldType.SizeBytes,
                    Semantic = fieldType.Semantic,
                    Type = fieldType,
                    Attributes = variable != IntPtr.Zero ? ReadVariableAttributes(variable) : [],
                });
            }
            fields = fieldList.ToArray();
        }
        else if (kind is ShaderTypeKind.ConstantBuffer or ShaderTypeKind.Array or ShaderTypeKind.ParameterBlock or ShaderTypeKind.ShaderStorageBuffer)
        {
            IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
            if (elementLayout != IntPtr.Zero)
                elementType = ReadTypeLayout(elementLayout, depth + 1);
            if (kind == ShaderTypeKind.Array)
            {
                strideBytes = (uint)SlangNative.spReflectionTypeLayout_getStride(typeLayout, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                if (type != IntPtr.Zero)
                    elementCount = (uint)SlangNative.spReflectionType_GetElementCount(type);
            }
        }
        else if (kind == ShaderTypeKind.Pointer && depth < 4)
        {
            IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
            if (elementLayout != IntPtr.Zero)
                elementType = ReadTypeLayout(elementLayout, depth + 1);
        }

        return new ShaderTypeLayout
        {
            Kind = kind,
            TypeName = typeName,
            SizeBytes = size,
            AlignmentBytes = alignment,
            StrideBytes = strideBytes,
            ElementCount = elementCount,
            ElementType = elementType,
            ScalarType = scalarType,
            VectorLength = vectorLength,
            MatrixRows = matrixRows,
            MatrixColumns = matrixColumns,
            MatrixLayout = matrixLayout,
            Semantic = SemanticOf(typeName),
            Fields = fields,
            Attributes = attributes,
        };
    }

    private static ShaderFieldSemantic SemanticOf(string? typeName) => typeName switch
    {
        "GPUBufferView" => ShaderFieldSemantic.BufferView,
        "GPUTextureId" => ShaderFieldSemantic.TextureId,
        "GPUSamplerId" => ShaderFieldSemantic.SamplerId,
        _ => ShaderFieldSemantic.Data,
    };

    private static ShaderAttribute[] ReadVariableAttributes(IntPtr variable)
    {
        uint count = SlangNative.spReflectionVariable_GetUserAttributeCount(variable);
        if (count == 0)
            return [];
        ShaderAttribute[] attributes = new ShaderAttribute[count];
        for (uint i = 0; i < count; i++)
            attributes[i] = ReadAttribute(SlangNative.spReflectionVariable_GetUserAttribute(variable, i));
        return attributes;
    }

    private static ShaderAttribute[] ReadTypeAttributes(IntPtr type)
    {
        uint count = SlangNative.spReflectionType_GetUserAttributeCount(type);
        if (count == 0)
            return [];
        ShaderAttribute[] attributes = new ShaderAttribute[count];
        for (uint i = 0; i < count; i++)
            attributes[i] = ReadAttribute(SlangNative.spReflectionType_GetUserAttribute(type, i));
        return attributes;
    }

    private static ShaderAttribute[] ReadFunctionAttributes(IntPtr function)
    {
        uint count = SlangNative.spReflectionFunction_GetUserAttributeCount(function);
        if (count == 0)
            return [];
        ShaderAttribute[] attributes = new ShaderAttribute[count];
        for (uint i = 0; i < count; i++)
            attributes[i] = ReadAttribute(SlangNative.spReflectionFunction_GetUserAttribute(function, i));
        return attributes;
    }

    private static ShaderAttribute ReadAttribute(IntPtr attribute)
    {
        if (attribute == IntPtr.Zero)
            return new ShaderAttribute { Name = string.Empty, Arguments = [] };
        string name = SlangNative.StringFromPtr(SlangNative.spReflectionUserAttribute_GetName(attribute)) ?? string.Empty;
        uint argCount = SlangNative.spReflectionUserAttribute_GetArgumentCount(attribute);
        ShaderAttributeArgument[] args = argCount == 0 ? [] : new ShaderAttributeArgument[argCount];
        for (uint i = 0; i < argCount; i++)
        {
            IntPtr argType = SlangNative.spReflectionUserAttribute_GetArgumentType(attribute, i);
            ShaderScalarType scalar = argType != IntPtr.Zero
                ? (ShaderScalarType)SlangNative.spReflectionType_GetScalarType(argType)
                : ShaderScalarType.None;
            string? str = SlangNative.StringFromPtr(SlangNative.spReflectionUserAttribute_GetArgumentValueString(attribute, i, IntPtr.Zero));
            if (str != null)
            {
                args[i] = new ShaderAttributeArgument
                {
                    ScalarType = ShaderScalarType.None,
                    StringValue = str,
                    DisplayValue = str,
                };
            }
            else if (scalar is ShaderScalarType.Float16 or ShaderScalarType.Float32 or ShaderScalarType.Float64)
            {
                float f = 0;
                SlangNative.spReflectionUserAttribute_GetArgumentValueFloat(attribute, i, &f);
                args[i] = new ShaderAttributeArgument
                {
                    ScalarType = scalar,
                    FloatValue = f,
                    DisplayValue = f.ToString(CultureInfo.InvariantCulture),
                };
            }
            else
            {
                int iv = 0;
                SlangNative.spReflectionUserAttribute_GetArgumentValueInt(attribute, i, &iv);
                args[i] = new ShaderAttributeArgument
                {
                    ScalarType = scalar,
                    IntValue = iv,
                    DisplayValue = scalar == ShaderScalarType.Bool ? (iv != 0 ? "true" : "false") : iv.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
        return new ShaderAttribute { Name = name, Arguments = args };
    }

    internal static ShaderStageKind ToPublicStage(SlangShaderStage stage) => (ShaderStageKind)(uint)stage;
    internal static SlangShaderStage ToInternalStage(ShaderStageKind stage) => (SlangShaderStage)(uint)stage;
}
