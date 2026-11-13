// Window glass on modelled façades. By day a dark pane with the sky in it; after dark a share of the windows are lit
// rooms. TEXCOORD0.x is 1000 + a random number per window written by FacadeBuilder.Panel, .y runs 0..1 up the opening,
// so a whole pane is lit or dark. Which windows are lit is typology.
Shader "Padova/Glass"
{
    Properties
    {
        _BaseColor("Glass", Color) = (0.09, 0.12, 0.15, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.94
        _Lit("Lit room", Color) = (1.0, 0.72, 0.4, 1)
        _Share("Share of windows lit at night", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Lit;
            half _Smoothness;
            half _Share;
        CBUFFER_END
        float _PadovaNight;
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 direct = main.color * (main.shadowAttenuation * main.distanceAttenuation);
                half3 color = _BaseColor.rgb * (SampleSH(n) + direct * saturate(dot(n, main.direction)));
                // The sky in the pane, stronger at a glancing angle.
                half fresnel = 0.05 + 0.95 * pow(1.0 - saturate(dot(n, view)), 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-view, n), i.positionWS, 1.0 - _Smoothness, 1.0h, GetNormalizedScreenSpaceUV(i.positionCS));
                color += sky * fresnel * _Smoothness;
                color += direct * pow(saturate(dot(n, normalize(main.direction + view))), 180.0) * _Smoothness;
                // Lit rooms: the window's own random number decides, so panes never split.
                // (Panes tagged by the façade builder carry 1000 + random; other glass, such as a windscreen, never lights.)
                half roll = frac(i.uv.x);
                half lit = step(999.5, i.uv.x) * step(roll, _PadovaNight * _Share);
                half warmth = frac(roll * 37.7);
                half3 room = lerp(_Lit.rgb, half3(1.0, 0.86, 0.66), warmth * 0.6) * (0.9 + 0.9 * frac(roll * 91.3));
                color = lerp(color, room * lerp(0.8, 1.15, saturate(i.uv.y)), lit);
                return half4(MixFog(color, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
