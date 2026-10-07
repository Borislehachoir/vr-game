// Affiche l'encre d'une WritableSurface par-dessus un fond semi-transparent.
// L'encre est stockée en alpha prémultiplié (voir BrushStamp.shader).
Shader "EscapeGame/WritableSurface"
{
    Properties
    {
        _InkTex ("Encre", 2D) = "black" {}
        _BackgroundColor ("Fond", Color) = (1, 1, 1, 0.25)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _InkTex;
            fixed4 _BackgroundColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 ink = tex2D(_InkTex, i.uv);
                fixed4 background = fixed4(_BackgroundColor.rgb * _BackgroundColor.a, _BackgroundColor.a);
                return ink + background * (1 - ink.a);
            }
            ENDCG
        }
    }
}
