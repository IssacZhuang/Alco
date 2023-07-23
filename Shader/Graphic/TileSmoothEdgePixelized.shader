Shader "Game/TileSmoothEdgePixelized"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        [ShowAsVector2] _PixelSize ("Pixel Size", Vector) = (16,16,0,0)
        _Scale ("Scale", Float) = 1
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType" = "Plane"}
        ColorMask RGB
        Lighting Off ZWrite Off

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
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
            
            float4 _Color;
            float _Scale;
            float2 _PixelSize;

            //round to nearest pixel
            fixed RoundX(float x)
            {
                float stepX = 1/_PixelSize.x;
                return round(x/stepX)*stepX;
            }

            fixed RoundY(float y)
            {
                float stepY = 1/_PixelSize.y;
                return round(y/stepY)*stepY;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex*_Scale);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv = v.vertex.xy*_Scale;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float extend = (_Scale-1)/2;
                float excess = max(RoundX(saturate(abs(i.uv.x)-0.5)), RoundY( saturate(abs(i.uv.y)-0.5)));
                if(extend>0)
                {
                    col.a = (extend-excess)/extend;
                }
                return col*_Color;
            }
            ENDCG
        }
    }
}
