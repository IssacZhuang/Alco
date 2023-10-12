using System;
using System.Numerics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Veldrid;

namespace Vocore.Engine
{



    public struct ShaderAnalyseResult
    {
        public const string PragmaKey_BlendState = "blend_state";
        public const string PragmaKey_DepthTest = "depth_test";
        public const string PragmaKey_DepthWrite = "depth_write";
        public const string PragmaKey_CullMode = "cull_mode";
        public const string PragmaKey_FillMode = "fill_mode";
        public const string PragmaKey_DepthClip = "depth_clip";
        public const string PragmaKey_ScissorTest = "scissor_test";
        public const string GLSL_True = "true";
        public const string GLSL_False = "false";
        //example: #pragma blend_state override_blend
        public const string RegexPragmaKeyValuePair = @"#pragma\s+(\w+)\s+(\w+)";
        //example: #pragma instance_buffer_start
        public const string RegexPragmaInstruction = @"#pragma\s+(\w+)";
        //exmaple: layout(location = 0) in vec3 position
        public const string RegexVertexLayout = @"layout\s*(\s*location\s*=\s*(\d+)\)\s*in\s+(\w+)\s+(\w+)";
        public const string InstructionInstanceBufferStart = "instance_buffer_start";
        public const string InstructionInstanceBufferEnd = "instance_buffer_end";
        public Dictionary<string, string> pragmaKeyValue;
        public List<VertexElementDescription> vertexLayouts;
        public List<VertexElementDescription> instanceLayouts;

        public ShaderAnalyseResult(string shaderText)
        {
            pragmaKeyValue = new Dictionary<string, string>();
            vertexLayouts = new List<VertexElementDescription>();
            instanceLayouts = new List<VertexElementDescription>();
            AnalyseShaderText(shaderText);
        }

        private static readonly Dictionary<string, ShaderDataType> DataTypeCast = new Dictionary<string, ShaderDataType>{
            {"float", ShaderDataType.Float},
            {"vec2", ShaderDataType.Float2},
            {"vec3", ShaderDataType.Float3},
            {"vec4", ShaderDataType.Float4},
            {"double", ShaderDataType.Double},
            {"dvec2", ShaderDataType.Double2},
            {"dvec3", ShaderDataType.Double3},
            {"dvec4", ShaderDataType.Double4},
            {"int", ShaderDataType.Int},
            {"ivec2", ShaderDataType.Int2},
            {"ivec3", ShaderDataType.Int3},
            {"ivec4", ShaderDataType.Int4},
            {"bool", ShaderDataType.Bool},
            {"bvec2", ShaderDataType.Bool2},
            {"bvec3", ShaderDataType.Bool3},
            {"bvec4", ShaderDataType.Bool4},
            {"mat2", ShaderDataType.Matrix2x2},
            {"mat3", ShaderDataType.Matrix3x3},
            {"mat4", ShaderDataType.Matrix4x4},
            {"sampler1D", ShaderDataType.Texture1D},
            {"sampler2D", ShaderDataType.Texture2D},
            {"sampler3D", ShaderDataType.Texture3D},
            {"samplerCube", ShaderDataType.TextureCube},
        };

        private static readonly Dictionary<ShaderDataType, VertexElementFormat> DataTypeVertexElementCast = new Dictionary<ShaderDataType, VertexElementFormat>{
            {ShaderDataType.Float, VertexElementFormat.Float1},
            {ShaderDataType.Float2, VertexElementFormat.Float2},
            {ShaderDataType.Float3, VertexElementFormat.Float3},
            {ShaderDataType.Float4, VertexElementFormat.Float4},
            {ShaderDataType.Int, VertexElementFormat.Int1},
            {ShaderDataType.Int2, VertexElementFormat.Int2},
            {ShaderDataType.Int3, VertexElementFormat.Int3},
            {ShaderDataType.Int4, VertexElementFormat.Int4},
            {ShaderDataType.Bool2, VertexElementFormat.Byte2},
            {ShaderDataType.Bool4, VertexElementFormat.Byte4},
        };

        private static readonly Dictionary<string, BlendStateDescription> BlendStateCast = new Dictionary<string, BlendStateDescription>{
            {"override_blend", BlendStateDescription.SingleOverrideBlend},
            {"alpha_blend", BlendStateDescription.SingleAlphaBlend},
            {"additive_blend", BlendStateDescription.SingleAdditiveBlend},
            {"disabled", BlendStateDescription.SingleDisabled},
            {"empty", BlendStateDescription.Empty},
        };

        private static readonly Dictionary<string, FaceCullMode> CullModeCast = new Dictionary<string, FaceCullMode>{
            {"none", FaceCullMode.None},
            {"front", FaceCullMode.Front},
            {"back", FaceCullMode.Back},
        };

        private static readonly Dictionary<string, PolygonFillMode> FillModeCast = new Dictionary<string, PolygonFillMode>{
            {"solid", PolygonFillMode.Solid},
            {"wireframe", PolygonFillMode.Wireframe},
        };

        public BlendStateDescription GetBlendState()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_BlendState, out string? value))
            {
                if (BlendStateCast.TryGetValue(value, out BlendStateDescription result))
                {
                    return result;
                }
            }
            //default value
            return BlendStateDescription.SingleOverrideBlend;
        }

        public bool GetDepthTestEnable()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_DepthTest, out string? value))
            {
                return value == GLSL_True;
            }
            //default value
            return true;
        }

        public bool GetDepthWriteEnable()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_DepthWrite, out string? value))
            {
                return value == GLSL_True;
            }
            //default value
            return true;
        }

        public bool GetDepthClipEnable()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_DepthClip, out string? value))
            {
                return value == GLSL_True;
            }
            //default value
            return true;
        }

        public bool GetScissorTestEnable()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_ScissorTest, out string? value))
            {
                return value == GLSL_True;
            }
            //default value
            return false;
        }

        public FaceCullMode GetCullMode()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_CullMode, out string? value))
            {
                if (CullModeCast.TryGetValue(value, out FaceCullMode result))
                {
                    return result;
                }
            }
            //default value
            return FaceCullMode.Back;
        }

        public PolygonFillMode GetFillMode()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_FillMode, out string? value))
            {
                if (FillModeCast.TryGetValue(value, out PolygonFillMode result))
                {
                    return result;
                }
            }
            //default value
            return PolygonFillMode.Solid;
        }



        public static ShaderDataType GetShaderDataType(string type)
        {
            if (DataTypeCast.TryGetValue(type, out ShaderDataType result))
            {
                return result;
            }
            return ShaderDataType.None;
        }

        private void AnalyseShaderText(string shader)
        {
            Dictionary<string, string> pragma = pragmaKeyValue;
            string[] lines = shader.Split('\n');
            List<VertexElementDescription> _vertexLayouts = this.vertexLayouts;
            List<VertexElementDescription> _instanceLayouts = this.instanceLayouts;
            List<VertexElementDescription> currentLayout = _vertexLayouts;
            foreach (string line in lines)
            {
                MatchLine(line, RegexPragmaKeyValuePair, (match) =>
                {
                    string key = match.Groups[1].Value;
                    string value = match.Groups[2].Value;
                    pragma.Add(key, value);
                });

            
            }

        }


        private VertexElementDescription CreateVertexLayoutDescription(string name, ShaderDataType type)
        {
            if (DataTypeVertexElementCast.TryGetValue(type, out VertexElementFormat format))
            {
                return new VertexElementDescription(name, format, VertexElementSemantic.TextureCoordinate);
            }

            throw new Exception("Invalid shader data type for vertex : " + type);
        }

        private void MatchLine(string line, string regex, Action<Match> action)
        {
            Match match = Regex.Match(line, regex);
            if (match.Success)
            {
                action(match);
            }
        }
    }
}

