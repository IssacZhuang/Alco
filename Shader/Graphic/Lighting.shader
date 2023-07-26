Shader "Game/Lighting"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        [HDR] _EnvironmentLight ("Environment Light", Color) = (1,1,1,1)
        _KawaseOffset ("Kawase Offset", Range(0, 1)) = 0.5

    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType" = "Plane"}
        ColorMask RGB
        Lighting Off ZWrite Off

        Pass
        {
            Blend DstColor SrcColor   
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _KawaseOffset;
            
            float4 _Color;
            float4 _EnvironmentLight;

            float4 kawaseBlur(sampler2D blurTexture, float2 uv, float offset, float2 res)
            {
                float i = offset;

                float4 col;
                col.rgb = tex2D(blurTexture, saturate(uv)).rgb;
                col.rgb += tex2D(blurTexture, saturate(uv + float2(i, i) * res)).rgb;
                col.rgb += tex2D(blurTexture, saturate(uv + float2(i, -i) * res)).rgb;
                col.rgb += tex2D(blurTexture, saturate(uv + float2(-i, i) * res)).rgb;
                col.rgb += tex2D(blurTexture, saturate(uv + float2(-i, -i) * res)).rgb;
                col.rgb /= 5.0f;

                col.a = 1;

                return col;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = kawaseBlur(_MainTex, i.uv, _KawaseOffset, _MainTex_TexelSize.xy);
                //fixed4 col = tex2D(_MainTex, i.uv);
                return col*_Color + _EnvironmentLight;
            }
            ENDCG
        }
    }
}
