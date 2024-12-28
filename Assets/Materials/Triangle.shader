Shader "Unlit/Triangle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Opacity ("Opacity", Float) = 0.0
        _Accel ("Acceleration", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Accel;
            float _Opacity;

            float sdEquilateralTriangle( in float2 p, in float r )
            {
                const float k = sqrt(3.0);
                p.x = abs(p.x) - r;
                p.y = p.y + r/k;
                if( p.x+k*p.y>0.0 ) p = float2(p.x-k*p.y,-k*p.x-p.y)/2.0;
                p.x -= clamp( p.x, -2.0*r, 0.0 );
                return -length(p)*sign(p.y);
            }


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv.xy * 2.0 - 1.0;
                uv *= 5.0;
                float mask = sdEquilateralTriangle(uv, 3);

                float fill = step(1.0-_Accel, 1.0 -(i.uv.y - 0.3));

                float outline = sdEquilateralTriangle(uv, 4);

                float colMask = 0.0125 + _Opacity * 0.3 + fill;

                float4 col = float4(colMask,colMask,colMask,1);

                mask = mask + outline * step(.5, _Accel);

                if ((1.0-mask) < .9){
                    discard;
                }

                return col;
            }
            ENDCG
        }
    }
}
