// Tampon rond dessiné dans la texture d'encre d'une WritableSurface (via GL, depuis WritableSurface.cs).
// Les sommets arrivent directement en coordonnées de texture (0-1), sans matrice de caméra.
// Sortie en alpha prémultiplié ; le mode de mélange choisit encre (One) ou effacement (Zero).
Shader "Hidden/EscapeGame/BrushStamp"
{
    Properties
    {
        _Color ("Couleur", Color) = (0.1, 0.25, 0.85, 1)
        _Softness ("Douceur du bord", Range(0.01, 1)) = 0.35
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 _Color;
            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float2 p = v.vertex.xy * 2 - 1;
            #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y; // garde v = 0 en bas de la texture sur toutes les plateformes
            #endif
                o.pos = float4(p, 0, 1);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float d = length(i.uv * 2 - 1);
                float coverage = saturate((1 - d) / _Softness) * _Color.a;
                return fixed4(_Color.rgb * coverage, coverage);
            }
            ENDCG
        }
    }
}
