// The gray box's lit surface, with the halo cutout built in — cutaway slice 2. Everything
// that can stand between the camera and the player wears this: tile walls and posts (guide
// texture as albedo), props (plain colour, white default texture). Matte lambert plus
// ambient, exactly the OverworldGround lighting model, because gray-box rock does not gleam
// and the two shaders must disagree about nothing except where albedo comes from.
//
// The halo cuts ForwardLit and DepthOnly and NEVER ShadowCaster — the hole is a courtesy to
// the camera, not a hole in the world, so shadows stay whole and light does not leak.
Shader "Prophecy/GrayBoxLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // Two-sided, so a halo hole through a wall shows the shell's INTERIOR instead
            // of tunnelling to whatever lies beyond it (the under-terrain water plane read
            // as a blue void — Matt's screenshot, 2026-08-07). Backfaces shade near-black:
            // through any hole, the inside of the world is dark rock.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HaloCutout.hlsl"
            #include "CaveInvertMask.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input, bool isFront : SV_IsFrontFace) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb
                               * _BaseColor.rgb;

                // The halo cuts BOTH faces: the cone must drill through everything between
                // the camera and the player — exempting interior faces left the player
                // sealed behind the first shell's inside (Matt: "only cutting the first
                // thing it hits"). The cone's tight aim is what keeps walls beside the
                // player whole now, not this exemption.
                ApplyHaloCutout(input.positionWS, input.positionCS);

                // A SURVIVING backface is the world's inside, seen through a halo hole from
                // outside the cone — near-black, so the shell reads solid.
                if (!isFront)
                    return half4(ApplyCaveInvert(albedo * 0.06h, input.positionWS), 1);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                half3 normal = normalize(input.normalWS);
                half lambert = saturate(dot(normal, light.direction));
                half3 lit = albedo * (light.color * (light.shadowAttenuation * lambert) +
                                      SampleSH(normal));

                // Inside a cave, everything outside the active room goes to the void.
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
