// The overworld's ground: albedo comes from the baked biome LUT (one texel per cell, world-XZ
// mapped), cross-faded by a MANUAL bilinear blend — biome INDICES cannot be filtered, so the
// shader taps the four nearest texels, resolves each to a colour, and blends the colours.
// A triplanar detail texture (white by default) is the hook real ground art lands on without
// touching the tiles' UVs, which remain authoring guides. Alpha channel of the LUT is reserved
// for the Consumed darkening.
Shader "Prophecy/OverworldGround"
{
    Properties
    {
        _GroundLut("Biome LUT", 2D) = "black" {}
        _DetailTex("Detail (triplanar)", 2D) = "white" {}
        _DetailScale("Detail Scale", Float) = 0.25
        _DefaultGround("Default Ground", Color) = (0.47, 0.68, 0.45, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _GroundLut_TexelSize;   // x=1/w y=1/h z=w w=h
        float4 _LutRect;               // xy = world origin XZ, zw = 1 / world size
        float _DetailScale;
        half4 _DefaultGround;
        half4 _BiomeColors[16];
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HaloCutout.hlsl"
            #include "CaveInvertMask.hlsl"

            TEXTURE2D(_GroundLut);
            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);

            half3 BiomeColor(uint index)
            {
                return index >= 16 ? _DefaultGround.rgb : _BiomeColors[index].rgb;
            }

            half3 TexelColor(int2 texel)
            {
                float4 data = _GroundLut.Load(int3(texel, 0));
                uint a = (uint)round(data.r * 255.0);
                uint b = (uint)round(data.g * 255.0);
                half3 colourA = BiomeColor(a);
                half3 colourB = b >= 16 ? colourA : BiomeColor(b);
                return lerp(colourA, colourB, (half)data.b);
            }

            half3 GroundColor(float2 worldXZ)
            {
                float2 uv = (worldXZ - _LutRect.xy) * _LutRect.zw;
                float2 texelF = uv * _GroundLut_TexelSize.zw - 0.5;
                int2 t0 = (int2)floor(texelF);
                float2 f = frac(texelF);
                int2 last = int2(_GroundLut_TexelSize.zw) - 1;
                int2 lo = clamp(t0, int2(0, 0), last);
                int2 hi = clamp(t0 + 1, int2(0, 0), last);

                half3 c00 = TexelColor(int2(lo.x, lo.y));
                half3 c10 = TexelColor(int2(hi.x, lo.y));
                half3 c01 = TexelColor(int2(lo.x, hi.y));
                half3 c11 = TexelColor(int2(hi.x, hi.y));
                return lerp(lerp(c00, c10, f.x), lerp(c01, c11, f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // The halo: a bridge deck is a cap, so the see-the-player-underneath hole
                // (cutaway slice 2) is cut right here in the ground shader.
                ApplyHaloCutout(input.positionWS, input.positionCS);

                half3 albedo = GroundColor(input.positionWS.xz);

                // Triplanar detail: three world-plane projections weighted by the normal —
                // no UVs involved, which is the whole point.
                float3 n = abs(normalize(input.normalWS));
                n /= max(n.x + n.y + n.z, 1e-4);
                half3 dx = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex,
                                            input.positionWS.zy * _DetailScale).rgb;
                half3 dy = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex,
                                            input.positionWS.xz * _DetailScale).rgb;
                half3 dz = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex,
                                            input.positionWS.xy * _DetailScale).rgb;
                albedo *= dx * n.x + dy * n.y + dz * n.z;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                half3 normal = normalize(input.normalWS);
                half lambert = saturate(dot(normal, light.direction));
                half3 lit = albedo * (light.color * (light.shadowAttenuation * lambert) +
                                      SampleSH(normal));

                // Inside a cave, everything outside the active room goes to the void —
                // applied here, where the fragment's world position is exact.
                lit = ApplyCaveInvert(lit, input.positionWS);
                return half4(lit, 1);
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
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

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
