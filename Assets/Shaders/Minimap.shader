// Circular, rotating minimap: samples the survey map texture around the player.
Shader "Padova/Minimap"
{
    Properties
    {
        _MainTex ("Map", 2D) = "white" {}
        _Center ("Centre (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Scale ("Span (UV)", Vector) = (0.1, 0.1, 0, 0)
        _Angle ("View heading (radians)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Center, _Scale;
            float _Angle;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float r = length(p);
                float s, c;
                sincos(_Angle, s, c);
                // Screen up shows the camera's heading (degrees clockwise from north).
                float2 q = float2(c * p.x + s * p.y, -s * p.x + c * p.y);
                float2 uv = _Center.xy + q * _Scale.xy;
                fixed4 col = tex2D(_MainTex, uv);
                if (any(uv < 0) || any(uv > 1)) col = fixed4(0.12, 0.14, 0.15, 1);
                col.rgb *= 0.92;
                float vignette = smoothstep(0.30, 0.5, r);
                col.rgb = lerp(col.rgb, col.rgb * 0.72, vignette);
                col.a = 0.94 * (1 - smoothstep(0.492, 0.5, r));
                return col;
            }
            ENDCG
        }
    }
}
