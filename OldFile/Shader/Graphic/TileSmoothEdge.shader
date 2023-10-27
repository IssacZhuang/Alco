Shader "Game/TileSmoothEdge"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _Extend ("Extend", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType" = "Plane" "DisableBatching" = "True"}
        ColorMask RGB
        Lighting Off ZWrite Off

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing  

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Extend)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v); 
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float scale = UNITY_ACCESS_INSTANCED_PROP(Props,_Extend) + 1;
                o.vertex = TransformObjectToHClip(v.vertex.xyz*scale);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv = v.vertex.xy*scale;

                
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float extend = UNITY_ACCESS_INSTANCED_PROP(Props,_Extend);
                float4 col = tex2D(_MainTex, i.uv);
                float halfExtend = extend/2;
                float excess = max(saturate(abs(i.uv.x)-0.5),  saturate(abs(i.uv.y)-0.5));
                if(extend>0)
                {
                    col.a = (halfExtend-excess)/halfExtend;
                }
                return col*UNITY_ACCESS_INSTANCED_PROP(Props,_Color);
            }
            ENDHLSL
        }
    }
}
