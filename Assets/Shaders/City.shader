// One material for the streamed city at distance: ground, roofs, shell walls and trees in a single draw per tile.
// Vertex colour rgb tints a surface layer (alpha * 255 selects the texture-array slice); TEXCOORD1 carries a
// façade grid (x: bay cells along the wall, y: storeys, z: window width as a share of the bay, w: per-building seed)
// from which windows, shutters and lit rooms are drawn procedurally. All of this is typology, not survey.
Shader "Padova/City"
{
    Properties
    {
        _Layers("Surface layers", 2DArray) = "white" {}
        _Glass("Window glass", Color) = (0.07, 0.09, 0.12, 1)
        _Lit("Lit room", Color) = (1.0, 0.74, 0.42, 1)
        _ShutterA("Shutter green", Color) = (0.13, 0.25, 0.17, 1)
        _ShutterB("Shutter brown", Color) = (0.32, 0.2, 0.13, 1)
        _WaterLayer("Water layer index", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D_ARRAY(_Layers);
        SAMPLER(sampler_Layers);
        CBUFFER_START(UnityPerMaterial)
            float4 _Layers_ST;
            half4 _Glass;
            half4 _Lit;
            half4 _ShutterA;
            half4 _ShutterB;
            float _WaterLayer;
        CBUFFER_END
        float _PadovaNight;   // 0 day .. 1 night, published by the day/night controller

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 facade : TEXCOORD1;
            half4 color : COLOR;
        };

        float hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 facade : TEXCOORD3;
                half4 color : COLOR;
                half fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.facade = v.facade;
                // Tints are authored in sRGB like the detailed materials.
                #ifdef UNITY_COLORSPACE_GAMMA
                    o.color = v.color;
                #else
                    o.color = half4(SRGBToLinear(v.color.rgb), v.color.a);
                #endif
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float layer = round(i.color.a * 255.0);
                half3 n = normalize(i.normalWS);
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 albedo;
                half3 emission = 0;
                half gloss = 0;
                if (abs(layer - _WaterLayer) < 0.5)
                {
                    // Slow canal water: two scrolling samples bend the normal; colour comes from the tint.
                    float2 flow = float2(_Time.y * 0.035, _Time.y * 0.021);
                    half a = SAMPLE_TEXTURE2D_ARRAY(_Layers, sampler_Layers, i.uv + flow, layer).r;
                    half b = SAMPLE_TEXTURE2D_ARRAY(_Layers, sampler_Layers, i.uv * 1.7 - flow * 1.3, layer).r;
                    n = normalize(half3((a - 0.5) * 0.35, 1, (b - 0.5) * 0.35));
                    half fresnel = pow(1.0 - saturate(dot(n, view)), 4.0);
                    albedo = lerp(i.color.rgb * (0.75 + 0.5 * a * b), half3(0.55, 0.66, 0.74), fresnel * 0.65);
                    gloss = 1;
                }
                else
                {
                    albedo = SAMPLE_TEXTURE2D_ARRAY(_Layers, sampler_Layers, i.uv, layer).rgb * i.color.rgb;
                }

                // ---- procedural façade
                if (i.facade.z > 0.001)
                {
                    float2 cell = floor(i.facade.xy);
                    float2 f = frac(i.facade.xy);
                    float2 w = fwidth(i.facade.xy);
                    half fade = saturate(1.6 - max(w.x, w.y) * 2.2);          // cells smaller than ~2 px melt into an average
                    half groundFloor = step(i.facade.y, 1.0);
                    half share = lerp(i.facade.z, min(0.82, i.facade.z * 2.1), groundFloor);
                    half x = abs(f.x - 0.5) * 2.0;
                    half sill = lerp(0.29, 0.0, groundFloor), head = lerp(0.85, 0.72, groundFloor);
                    half ax = max(w.x * 2.0, 0.002), ay = max(w.y, 0.002);
                    half inX = 1.0 - smoothstep(share - ax, share + ax, x);
                    half inY = smoothstep(sill - ay, sill + ay, f.y) * (1.0 - smoothstep(head - ay, head + ay, f.y));
                    half window = inX * inY;
                    half seed = i.facade.w;
                    half roll = hash21(cell + seed * 37.0);
                    // Frame, glass with a hint of sky, and shutters folded back beside most upper windows.
                    half frame = saturate((x - (share - 0.08)) * 12.0) * (1.0 - groundFloor);
                    half3 glass = lerp(_Glass.rgb, half3(0.42, 0.5, 0.6), saturate(f.y - 0.35) * 0.45 * (1.0 - _PadovaNight));
                    half3 opening = lerp(glass, half3(0.85, 0.84, 0.8), frame * 0.8);
                    half closed = step(roll, 0.16) * (1.0 - groundFloor);
                    half3 shutter = lerp(_ShutterA.rgb, _ShutterB.rgb, step(0.62, seed));
                    opening = lerp(opening, shutter * (0.8 + 0.2 * step(0.5, frac(f.y * 22.0))), closed);
                    half wing = (1.0 - groundFloor) * step(seed, 0.82) * step(0.16, roll) * inY
                              * smoothstep(share - ax, share + ax, x) * (1.0 - smoothstep(share * 1.95 - ax, share * 1.95 + ax, x));
                    half3 detailed = lerp(albedo, shutter, wing);
                    detailed = lerp(detailed, opening, window);
                    // String course above the ground floor.
                    detailed *= 1.0 - 0.18 * (1.0 - smoothstep(0.0, 0.035 + ay, abs(i.facade.y - 1.0)));
                    half coverage = share * 0.5 * (1.0 - groundFloor) + share * 0.7 * groundFloor;
                    half3 average = lerp(albedo, _Glass.rgb * 1.6, coverage * 0.75);
                    albedo = lerp(average, detailed, fade);
                    half lit = step(roll, _PadovaNight * 0.55) * (1.0 - closed);
                    emission = _Lit.rgb * (0.7 + 0.6 * hash21(cell.yx + seed)) * lit * lerp(coverage, window, fade) * 1.6;
                }

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light main = GetMainLight(shadowCoord);
                half3 ambient = SampleSH(n);
                half3 direct = main.color * (main.shadowAttenuation * main.distanceAttenuation);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ambient *= ao.indirectAmbientOcclusion;
                    direct *= ao.directAmbientOcclusion;
                #endif
                half ndl = saturate(dot(n, main.direction));
                half3 color = albedo * (ambient + direct * ndl) + emission;
                half3 halfway = normalize(main.direction + view);
                color += direct * gloss * pow(saturate(dot(n, halfway)), 90.0) * 0.6;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
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
