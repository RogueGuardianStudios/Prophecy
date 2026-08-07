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
                // every active room — exactly the void the style wants.
                float2 uv = (world.xz - _CoverLutRect.xy) * _CoverLutRect.zw;
                float id = 0.0;
                if (all(uv >= 0.0) && all(uv <= 1.0))
                    id = SAMPLE_TEXTURE2D_LOD(_CoverLut, sampler_CoverLut, uv, 0).r * 255.0;

                float inside = abs(id - _ActiveCoverRegion) < 0.5 ? 1.0 : 0.0;
                color.rgb *= 1.0 - _CaveInvertStrength * (1.0 - inside);
                return color;
            }
            ENDHLSL
        }
    }
}
