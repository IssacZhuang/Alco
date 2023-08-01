using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ExceptionRendering
    {
        public readonly static Exception MaterialNotInstanced = new Exception("The GPU instancing of material is not enabled");
        public readonly static Exception MeshIsMissing = new Exception("The mesh is null");
        public readonly static Exception MaterialIsMissing = new Exception("The material is null");
        public readonly static Exception ShaderIsMissing = new Exception("The shader is null");

        public static Exception ShaderNotFound(string shaderName)
        {
            return new Exception("Shader not found: " + shaderName);
        }

        public static Exception InvalidRenderId(int from, int to, int value)
        {
            return new Exception("Invalid render ID: " + value + ". The render ID should start from " + from + " to " + to);
        }
    }
}
