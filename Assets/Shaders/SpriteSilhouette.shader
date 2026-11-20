// 스프라이트의 모양(알파)만 살리고 색은 한 가지로 칠하는 셰이더.
// 상호작용 강조 테두리(HoverHighlight)에 쓴다. 색은 SpriteRenderer.color로 정한다.
// 조명을 받지 않는 Unlit이라 어두운 방에서도 하얗게 빛나 보인다.
Shader "Improv/SpriteSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;     // SpriteRenderer.color가 여기로 들어온다
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 텍스처에서 투명도만 가져오고, 색은 지정한 색으로 채운다.
                fixed alpha = tex2D(_MainTex, i.uv).a;
                return fixed4(i.color.rgb, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
