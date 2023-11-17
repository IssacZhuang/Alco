using System;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class Shader : IDisposable
    {
        private readonly Pipeline _pipeline;
        private readonly SpirvReflection _reflection;
        private readonly string _name;

        public Pipeline Pipeline
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _pipeline;
        }

        public Shader(string name, Pipeline pipeline, SpirvReflection reflection)
        {
            _pipeline = pipeline;
            _reflection = reflection;
            _name = name;
        }

        public void Dispose()
        {
            _pipeline.Dispose();
        }

        public string GetReflectionInfo()
        {
            string result = $"Shader [{_name}]\n";
            result += "Vertex Elements:\n";
            for (int i = 0; i < _reflection.VertexElements.Length; i++)
            {
                var element = _reflection.VertexElements[i];
                result += $"[{i}] {element.Format} {element.Name} {element.Semantic}\n";
            }
            result += "Resource Layouts:\n";
            for (int i = 0; i < _reflection.ResourceLayouts.Length; i++)
            {
                for (int j = 0; j < _reflection.ResourceLayouts[i].Elements.Length; j++)
                {
                    var element = _reflection.ResourceLayouts[i].Elements[j];
                    result += $"[{i}, {j}] {element.Kind} {element.Name}\n";
                }
            }
            return result;
        }
    }
}

