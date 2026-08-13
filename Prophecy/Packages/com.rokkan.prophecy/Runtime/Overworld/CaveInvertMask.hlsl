// The invert cutout's mask, applied IN the geometry shaders — take two of the cave look.
// Take one was a fullscreen pass reconstructing world positions from the depth texture; it
// worked until the whole world moved onto our own shaders, then the depth texture came up
// empty and an evening died diagnosing it. This version needs none of that: every fragment
// already KNOWS its world position exactly, so the room test is a texture tap and a compare —
// no depth, no inverse view-projection, no per-camera hand-off, no RenderGraph binding rules.
// The void outside the world's silhouette is the camera's own background, faded to black by
// the reveal driver.
//
// Globals, set by CaveRevealDriver and the world builder — ordinary geometry passes read
// texture globals fine (it is only RenderGraph fullscreen passes that refuse them).
#ifndef PROPHECY_CAVE_INVERT_MASK_INCLUDED
#define PROPHECY_CAVE_INVERT_MASK_INCLUDED

TEXTURE2D(_CoverLut);
SAMPLER(sampler_CoverLut);
float4 _CoverLutRect;        // xy: world origin XZ; zw: 1 / world size
float  _ActiveCoverRegion;   // room id + 1; 0 = no room active
float  _CaveInvertStrength;  // 0..1, the driver's fade
float  _CaveRoofY;           // the active room's roof plane, world Y

half3 ApplyCaveInvert(half3 color, float3 positionWS)
{
    if (_CaveInvertStrength <= 0.001 || _ActiveCoverRegion < 0.5)
        return color;

    // Four taps a sliver out, OR-ed: wall faces sit exactly on cell boundaries, and a
    // single point sample there can shimmer with interpolation rounding. The sliver is
    // small on purpose — half a cell would light ribbons of the surrounding terrain.
    float2 uv = (positionWS.xz - _CoverLutRect.xy) * _CoverLutRect.zw;
    float inside = 0.0;
    [unroll]
    for (int tap = 0; tap < 4; tap++)
    {
        float2 offset = float2(tap < 2 ? -0.15 : 0.15,
                               tap % 2 == 0 ? -0.15 : 0.15) * _CoverLutRect.zw;
        float2 tapUv = uv + offset;
        if (any(tapUv < 0.0) || any(tapUv > 1.0)) continue;
        float id = SAMPLE_TEXTURE2D_LOD(_CoverLut, sampler_CoverLut, tapUv, 0).r * 255.0;
        if (abs(id - _ActiveCoverRegion) < 0.5) inside = 1.0;
    }

    // The reveal exists BELOW the room's roof plane, full stop: the terrace tops around
    // and over the room are the outside world, however close their cells sit.
    if (positionWS.y > _CaveRoofY - 0.25) inside = 0.0;

    return color * (1.0 - _CaveInvertStrength * (1.0 - inside));
}

#endif
