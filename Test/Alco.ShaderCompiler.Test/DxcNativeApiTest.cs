using NUnit.Framework;

namespace Alco.ShaderCompiler;

[TestFixture]
public class DxcNativeApiTest
{
    private const string MinimalVertexShader = @"
float4 main(float4 pos : POSITION) : SV_POSITION
{
    return pos;
}";

    private const string MinimalComputeShader = @"
[numthreads(1, 1, 1)]
void main()
{
}";

    private const string InvalidShader = "this is not valid hlsl";

    [Test]
    public void DxcCreateInstance_Compiler_Succeeds()
    {
        IntPtr ptr = DXCNative.CreateInstance(
            DxcGuids.CLSID_DxcCompiler,
            DxcGuids.IID_IDxcCompiler3);
        Assert.That(ptr, Is.Not.EqualTo(IntPtr.Zero));

        var compiler = new DxcCompiler3(ptr);
        compiler.Release();
    }

    [Test]
    public void DxcCreateInstance_Utils_Succeeds()
    {
        IntPtr ptr = DXCNative.CreateInstance(
            DxcGuids.CLSID_DxcUtils,
            DxcGuids.IID_IDxcUtils);
        Assert.That(ptr, Is.Not.EqualTo(IntPtr.Zero));

        var utils = new DxcUtils(ptr);
        utils.Release();
    }

    [Test]
    public void Compile_MinimalVertexShader_ReturnsBytecode()
    {
        var options = new CompilerOptions(ShaderType.Vertex.ToProfile(6, 0))
        {
            entryPoint = "main",
        };
        CompilationResult result = ShaderCompiler.Compile(MinimalVertexShader, options);

        Assert.Multiple(() =>
        {
            Assert.That(result.compilationErrors, Is.Null);
            Assert.That(result.objectBytes, Is.Not.Null);
            Assert.That(result.objectBytes.Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Compile_MinimalComputeShader_ReturnsBytecode()
    {
        var options = new CompilerOptions(ShaderType.Compute.ToProfile(6, 0))
        {
            entryPoint = "main",
        };
        CompilationResult result = ShaderCompiler.Compile(MinimalComputeShader, options);

        Assert.Multiple(() =>
        {
            Assert.That(result.compilationErrors, Is.Null);
            Assert.That(result.objectBytes, Is.Not.Null);
            Assert.That(result.objectBytes.Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Compile_InvalidShader_ReturnsErrors()
    {
        var options = new CompilerOptions(ShaderType.Vertex.ToProfile(6, 0))
        {
            entryPoint = "main",
        };
        CompilationResult result = ShaderCompiler.Compile(InvalidShader, options);

        Assert.Multiple(() =>
        {
            Assert.That(result.compilationErrors, Is.Not.Null.And.Not.Empty);
            Assert.That(result.objectBytes, Is.Empty);
        });
    }

    [Test]
    public void Compile_WithIncludeHandler_CallbackWorks()
    {
        string includeContent = "float4 GetColor() { return float4(1, 0, 0, 1); }";
        string shaderWithInclude = @"
#include ""test.hlsli""
float4 main(float4 pos : POSITION) : SV_POSITION
{
    return GetColor();
}";

        bool handlerCalled = false;
        string? receivedName = null;

        string IncludeHandler(string name)
        {
            handlerCalled = true;
            receivedName = name;
            return includeContent;
        }

        var options = new CompilerOptions(ShaderType.Vertex.ToProfile(6, 0))
        {
            entryPoint = "main",
        };
        CompilationResult result = ShaderCompiler.Compile(shaderWithInclude, options, IncludeHandler);

        Assert.Multiple(() =>
        {
            Assert.That(handlerCalled, Is.True, "Include handler was not called");
            Assert.That(receivedName, Does.EndWith("test.hlsli"), $"Expected name ending with 'test.hlsli' but got '{receivedName}'");
            Assert.That(result.compilationErrors, Is.Null);
            Assert.That(result.objectBytes.Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Compile_SpirvTarget_ReturnsSpirvBytecode()
    {
        var options = new CompilerOptions(ShaderType.Vertex.ToProfile(6, 0))
        {
            entryPoint = "main",
            generateAsSpirV = true,
            optimization = OptimizationLevel.O0,
        };
        CompilationResult result = ShaderCompiler.Compile(MinimalVertexShader, options);

        Assert.Multiple(() =>
        {
            Assert.That(result.compilationErrors, Is.Null);
            Assert.That(result.objectBytes.Length, Is.GreaterThan(4));
            // SPIR-V magic number: 0x07230203 (little-endian bytes: 03 02 23 07)
            Assert.That(result.objectBytes[0], Is.EqualTo(0x03));
            Assert.That(result.objectBytes[1], Is.EqualTo(0x02));
            Assert.That(result.objectBytes[2], Is.EqualTo(0x23));
            Assert.That(result.objectBytes[3], Is.EqualTo(0x07));
        });
    }

    [Test]
    public void Compile_MultipleCompilations_NoMemoryLeak()
    {
        var options = new CompilerOptions(ShaderType.Vertex.ToProfile(6, 0))
        {
            entryPoint = "main",
        };

        for (int i = 0; i < 100; i++)
        {
            CompilationResult result = ShaderCompiler.Compile(MinimalVertexShader, options);
            Assert.That(result.compilationErrors, Is.Null, $"Compilation failed at iteration {i}");
            Assert.That(result.objectBytes.Length, Is.GreaterThan(0), $"Empty bytecode at iteration {i}");
        }
    }
}
