using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Pins the generic value-parameter reflection and literal specialization-argument contracts.
/// </summary>
[TestFixture]
public class GenericValueParameterTest
{
    private const string ThreeAxisShader = """
        [shader("fragment")]
        float4 MainPS<let flag : bool, let mode : int, let mask : uint>(float4 color : COLOR) : SV_TARGET
        {
            if (flag) { color.r += float(mode); }
            if ((mask & 3u) != 0u) { color.g += 0.25f; }
            return color;
        }
        """;

    private const string BoolAxisShader = """
        [shader("fragment")]
        float4 MainPS<let flag : bool>(float4 color : COLOR) : SV_TARGET
        {
            return flag ? color : color * 0.5f;
        }
        """;

    private const string IntAxisShader = """
        [shader("fragment")]
        float4 MainPS<let mode : int>(float4 color : COLOR) : SV_TARGET
        {
            color.r += float(mode) / 255.0f;
            return color;
        }
        """;

    private const string UIntAxisShader = """
        [shader("fragment")]
        float4 MainPS<let mask : uint>(float4 color : COLOR) : SV_TARGET
        {
            if ((mask & 3u) != 0u) { color.g = 1.0f; }
            return color;
        }
        """;

    // The shape that crashed slang 2026.16 natively (fixed in 2026.19):
    // ternary condition const-folds true from an int comparison.
    // See the fixture doc comment.
    private const string IntTernaryShader = """
        [shader("fragment")]
        float4 MainPS<let mode : int>(float4 color : COLOR) : SV_TARGET
        {
            return mode == 3 ? color : color * 0.5f;
        }
        """;

    private static SlangProgram Link(string source, params string[] args)
    {
        var compiler = new SlangCompiler();
        SlangCompileSession session = compiler.CreateSession(new SlangCompilerOptions());
        SlangModuleHandle module = session.LoadModuleFromSource(
            "alco_generic_value_probe", "alco_generic_value_probe.slang", source);
        return session.CompileAllEntryPoints(module, args);
    }

    /// <summary>
    /// Tests that generic value parameters are enumerated by name and scalar type.
    /// </summary>
    [Test]
    public void ValueParameters_EnumerateByNameAndScalarType()
    {
        using var compiler = new SlangCompiler();
        using SlangCompileSession session = compiler.CreateSession(new SlangCompilerOptions());
        SlangModuleHandle module = session.LoadModuleFromSource(
            "alco_generic_value_probe", "alco_generic_value_probe.slang", ThreeAxisShader);

        IntPtr moduleDecl = module.GetModuleDecl();
        uint childCount = SlangNative.spReflectionDecl_getChildrenCount(moduleDecl);
        IntPtr generic = IntPtr.Zero;
        for (uint i = 0; i < childCount; i++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(moduleDecl, i);
            if (SlangNative.spReflectionDecl_getKind(child) == SlangNative.SLANG_DECL_KIND_GENERIC)
            {
                generic = SlangNative.spReflectionDecl_castToGeneric(child);
                break;
            }
        }
        Assert.That(generic, Is.Not.EqualTo(IntPtr.Zero), "the generic entry point must be in the module decl tree");

        Assert.That(SlangNative.spReflectionGeneric_GetValueParameterCount(generic), Is.EqualTo(3u));
        Assert.That(SlangNative.spReflectionGeneric_GetTypeParameterCount(generic), Is.EqualTo(0u));

        string[] expectedNames = ["flag", "mode", "mask"];
        int[] expectedScalars =
        [
            SlangNative.SLANG_SCALAR_TYPE_BOOL,
            SlangNative.SLANG_SCALAR_TYPE_INT32,
            SlangNative.SLANG_SCALAR_TYPE_UINT32,
        ];
        for (uint i = 0; i < 3; i++)
        {
            IntPtr parameter = SlangNative.spReflectionGeneric_GetValueParameter(generic, i);
            Assert.That(parameter, Is.Not.EqualTo(IntPtr.Zero), $"value parameter {i} must reflect");
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(parameter));
            Assert.That(name, Is.EqualTo(expectedNames[i]), $"value parameter {i} name, in declaration order");
            IntPtr type = SlangNative.spReflectionVariable_GetType(parameter);
            Assert.That(type, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(SlangNative.spReflectionType_GetKind(type), Is.EqualTo(SlangNative.SLANG_TYPE_KIND_SCALAR));
            Assert.That(SlangNative.spReflectionType_GetScalarType(type), Is.EqualTo(expectedScalars[i]),
                $"value parameter '{expectedNames[i]}' scalar type");
        }
    }

    /// <summary>
    /// Tests that a bool axis specializes with the keyword form.
    /// </summary>
    [Test]
    public void BoolAxis_SpecializesWithKeyword()
    {
        using SlangProgram program = Link(BoolAxisShader, "true");
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }

    /// <summary>
    /// Tests that an int axis specializes with a decimal literal.
    /// </summary>
    [Test]
    public void IntAxis_SpecializesWithDecimal()
    {
        using SlangProgram program = Link(IntAxisShader, "3");
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }

    /// <summary>
    /// Tests that a uint axis specializes with a decimal literal.
    /// </summary>
    [Test]
    public void UIntAxis_SpecializesWithDecimal()
    {
        using SlangProgram program = Link(UIntAxisShader, "7");
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }

    /// <summary>
    /// Tests that three value axes specialize together.
    /// </summary>
    [Test]
    public void ThreeAxes_SpecializeTogether()
    {
        using SlangProgram program = Link(ThreeAxisShader, "true", "3", "7");
        Assert.That(program.EntryCode, Has.Length.EqualTo(1));
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }

    /// <summary>
    /// Tests that three value axes specialize with their defaults.
    /// </summary>
    [Test]
    public void ThreeAxes_SpecializeWithDefaults()
    {
        // The defaults an absent axis resolves to: false / 0 / 0.
        using SlangProgram program = Link(ThreeAxisShader, "false", "0", "0");
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }

    /// <summary>
    /// Tests that an int axis with a constant-true ternary condition compiles.
    /// </summary>
    [Test]
    public void IntAxis_TernaryTrueCondition_Compiles()
    {
        // Crashed slang 2026.16 natively; guards the fix in 2026.19+.
        using SlangProgram program = Link(IntTernaryShader, "3");
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }
}
