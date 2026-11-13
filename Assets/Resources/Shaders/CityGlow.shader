// Pools of lamplight on the ground: soft additive discs, scaled by the global _PadovaNight (the streamer does not
// draw them at all by day). Vertex colour is the lamp colour, uv runs 0..1 across the disc.
Shader "Padova/CityGlow"
{
    Properties
    {
        _Gain("Brightness", Float) = 0.1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Gain;
            CBUFFER_END
            float _PadovaNight;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 color : COLOR; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * 2.0 - 1.0;
                o.color = SRGBToLinear(v.color.rgb);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half falloff = saturate(1.0 - dot(i.uv, i.uv));
                half3 light = i.color * (falloff * falloff * _Gain * _PadovaNight * ComputeFogIntensity(i.fog));
                return half4(light, 0);
            }
            ENDHLSL
        }
    }
}
