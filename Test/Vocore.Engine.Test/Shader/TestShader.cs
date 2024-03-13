using NUnit.Framework;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestShader
{
    [Test(Description ="Shader pragmas")]
    public void TestShaderPragmas()
    {
        string shaderText = @"
                // Some comments
                #pragma pragma1 value1 value2
                #pragma pragma2 value3 value4
                // More comments
                #pragma pragma3 value5
                // Commented pragma, should not be parsed
                //#pragma pragma4 value6

                /* comment block
                #pragma pragma5 value7
                */
                ";

        // Act
        ShaderPragma[] pragmas = UtilsShaderText.GetShaderPragma(shaderText);

        // Assert
        Assert.That(pragmas.Length, Is.EqualTo(3));

        Assert.That(pragmas[0].Name, Is.EqualTo("pragma1"));
        Assert.That(pragmas[0].Values.Length, Is.EqualTo(2));
        Assert.That(pragmas[0].Values[0], Is.EqualTo("value1"));
        Assert.That(pragmas[0].Values[1], Is.EqualTo("value2"));

        Assert.That(pragmas[1].Name, Is.EqualTo("pragma2"));
        Assert.That(pragmas[1].Values.Length, Is.EqualTo(2));
        Assert.That(pragmas[1].Values[0], Is.EqualTo("value3"));
        Assert.That(pragmas[1].Values[1], Is.EqualTo("value4"));

        Assert.That(pragmas[2].Name, Is.EqualTo("pragma3"));
        Assert.That(pragmas[2].Values.Length, Is.EqualTo(1));
        Assert.That(pragmas[2].Values[0], Is.EqualTo("value5"));
    }

    [Test(Description = "Shader no pragmas")]
    public void TestShaderNoPragmas(){
        string shaderText = @"
                // Some comments
                // More comments";

        // Act
        ShaderPragma[] pragmas = UtilsShaderText.GetShaderPragma(shaderText);

        // Assert
        Assert.That(pragmas.Length, Is.EqualTo(0));
    }

}
