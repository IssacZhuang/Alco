using System;
using System.Numerics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Veldrid;

namespace Vocore.Engine
{


    public struct VertexLayout
    {
        public string name;
        public int index;
        public ShaderDataType type;
    }



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
        public Dictionary<string, string> pragmaKeyValue;

        public ShaderAnalyseResult(string shaderText)
        {
            pragmaKeyValue = GetPragma(shaderText);
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

        public static Dictionary<string, string> GetPragma(string shader)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string[] lines = shader.Split('\n');
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, RegexPragmaKeyValuePair);
                if (match.Success)
                {
                    result.Add(match.Groups[1].Value, match.Groups[2].Value);
                }
            }
            return result;
        }
    }
}

