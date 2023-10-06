using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Rendering
{

    public static class ShaderLoader
    {
        public static string EntryPointVertex = "vertex";
        public static string EntryPointFragment = "fragment";


        public static ShaderDescription ComplieShader(byte[] shaderText, ShaderStages stage, string entryPoint = "main")
        {
            return new ShaderDescription(stage, shaderText, entryPoint);
        }

        public static ShaderDescription ComplieShader(string shaderText, ShaderStages stage, string entryPoint = "main")
        {
            return new ShaderDescription(stage, Encoding.UTF8.GetBytes(shaderText), entryPoint);
        }



    }
}

