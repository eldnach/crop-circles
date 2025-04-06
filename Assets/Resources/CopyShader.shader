Shader "Hidden/CopyShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            Texture2D<uint> _MainTex;


            uint encodeUint(float x, float y, float z, float w)
            {
                uint ux = (uint)(saturate(x) * 255.0);
                uint uy = (uint)(saturate(y) * 255.0);
                uint uz = (uint)(saturate(z) * 255.0);
                uint uw = (uint)(saturate(w) * 255.0);
                return ux | (uy << 8) | (uz << 16) | (uw << 24);
            }

            float4 decodeUint(uint u)
            {
                float fx = (float)(u & 0x000000ff) / 255.0;
                float fy = (float)((u >> 8) & 0x000000ff) / 255.0;
                float fz = (float)((u >> 16) & 0x000000ff) / 255.0;
                float fw = (float)((u >> 24) & 0x000000ff) / 255.0;
                return float4(fx, fy, fz, fw);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                uint3 sampleCoord = uint3(i.uv.x * 128, i.uv.y * 128, 0);
                uint encodedNormal = (uint)_MainTex.Load(sampleCoord);
               // uint encodedNormal =  (uint) tex2Dlod(_MainTex, float4(sampleCoord.xy, 0.0, 0.0));
                float4 decodedNormal = decodeUint(encodedNormal);
                //decodedNormal = float4(0.0, 1.0, 0.0, 0.0);
                fixed4 color = fixed4(decodedNormal.x, decodedNormal.y, decodedNormal.z, decodedNormal.w);

                return color;
            }
            ENDCG
        }
    }
}
