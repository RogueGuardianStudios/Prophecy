// The invert cutout's fullscreen pass — cutaway style 2 of 3 (Matt): inside a cave the
// overworld goes BLACK and the covered room is what you see. Runs as a FullScreenPass
// renderer feature: reconstruct each pixel's world position from depth, look up its cell in
// the cave-room mask (baked by the world builder), and drive everything outside the active
// room to black. The roof itself is hidden CPU-side by the reveal driver; this pass paints
// the void around the room. Strength 0 = a plain copy, so the pass is inert everywhere the
// driver says so (edit mode, outside, side-scroll).
//
// Every input arrives on the pass MATERIAL, set by CaveRevealDriver — texture globals never
// reach a RenderGraph fullscreen pass, and UNITY_MATRIX_I_VP here is the blit's matrix, not
// the camera's. Both were paid for on 2026-08-07; see the driver.
Shader "Prophecy/CaveInvert"
{
    // The Properties block is LOAD-BEARING: without it these are shader globals, and
    // Material.SetTexture on "_CoverLut" quietly fails to stick — the driver binds the
    // per-build LUT through the material, so the material must genuinely own the property.
    Properties
    {
        _CoverLut ("Cave Room Mask", 2D) = "black" {}
        _CoverLutRect ("Mask Rect", Vector) = (0, 0, 0, 0)
        _ActiveCoverRegion ("Active Room", Float) = 0
        _CaveInvertStrength ("Strength", Float) = 0
        _CaveRoofY ("Active Room Roof Height", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "CaveInvert"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_CoverLut);
            SAMPLER(sampler_CoverLut);
            float4 _CoverLutRect;        // xy: world origin XZ; zw: 1 / world size
            float  _ActiveCoverRegion;   // room id + 1; 0 = no room active
            float  _CaveInvertStrength;  // 0..1, the driver's fade
            float  _CaveRoofY;           // the active room's roof height, world Y
            float4x4 _CaveCamInvVP;      // the CAMERA's inverse view-projection

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp,
                                                  input.texcoord);
                if (_CaveInvertStrength <= 0.001 || _ActiveCoverRegion < 0.5)
                    return color;

                float depth = SampleSceneDepth(input.texcoord);
                float3 world = ComputeWorldSpacePosition(input.texcoord, depth, _CaveCamInvVP);

                // Outside the map rect (including the sky, whose far-plane world position
                // lands nowhere near it) counts as room 0 = "no cave", which is outside
                // every active room — exactly the void the style wants. Four taps a SLIVER
                // out, OR-ed: the room's WALLS sit exactly on cell boundaries, and a single
                // point sample there flickers with depth precision as the camera moves. The
                // sliver is deliberately small — half a cell lit ribbons of the surrounding
                // terrace TOP around the room, which read as ground beyond the map.
                float2 uv = (world.xz - _CoverLutRect.xy) * _CoverLutRect.zw;
                float inside = 0.0;
                [unroll]
                for (int tap = 0; tap < 4; tap++)
                {
                    float2 offset = float2(tap < 2 ? -0.15 : 0.15,
                                           tap % 2 == 0 ? -0.15 : 0.15) * _CoverLutRect.zw;
                    float2 tapUv = uv + offset;
                    if (any(tapUv < 0.0) || any(tapUv > 1.0)) continue;
                    float id = SAMPLE_TEXTURE2D_LOD(_CoverLut, sampler_CoverLut, tapUv, 0).r
                               * 255.0;
                    if (abs(id - _ActiveCoverRegion) < 0.5) inside = 1.0;
                }

                // The reveal exists BELOW the room's roof plane, full stop. Whatever the
                // mask says, the terrace tops around (and over) the room stay in the void —
                // they are the outside world, however close their cells sit.
                if (world.y > _CaveRoofY - 0.25) inside = 0.0;

                color.rgb *= 1.0 - _CaveInvertStrength * (1.0 - inside);
                return color;
            }
            ENDHLSL
        }
    }
}
