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
        public const string PragmaKey_TopologyPrimitive = "topology_primitive";
        public const string PragmaKey_DepthClip = "depth_clip";
        public const string PragmaKey_ScissorTest = "scissor_test";
        public const string GLSL_True = "true";
        public const string GLSL_False = "false";
        public const string RegexPragmaKeyValuePair = @"#pragma\s+(\w+)\s+(\w+)";
        public Dictionary<string, string> pragmaKeyValue;

        public ShaderAnalyseResult(string shaderText)
        {
            pragmaKeyValue = new Dictionary<string, string>();
            AnalyseShaderText(shaderText);
        }

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

        private static readonly Dictionary<string, PrimitiveTopology> TopologyPrimitiveCast = new Dictionary<string, PrimitiveTopology>{
            {"point_list", PrimitiveTopology.PointList},
            {"line_list", PrimitiveTopology.LineList},
            {"line_strip", PrimitiveTopology.LineStrip},
            {"triangle_list", PrimitiveTopology.TriangleList},
            {"triangle_strip", PrimitiveTopology.TriangleStrip},
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

        public PrimitiveTopology GetTopologyPrimitive()
        {
            if (pragmaKeyValue.TryGetValue(PragmaKey_TopologyPrimitive, out string? value))
            {
                if (TopologyPrimitiveCast.TryGetValue(value, out PrimitiveTopology result))
                {
                    return result;
                }
            }
            //default value
            return PrimitiveTopology.TriangleList;
        }


        private void AnalyseShaderText(string shader)
        {
            Dictionary<string, string> pragma = pragmaKeyValue;
            string[] lines = shader.Split('\n');
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, RegexPragmaKeyValuePair);

                if (match.Success)
                {
                    string key = match.Groups[1].Value;
                    string value = match.Groups[2].Value;
                    pragma.Add(key, value);
                }
            }
        }
    }
}

