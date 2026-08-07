// The halo cutout — cutaway style 1 of 3 (Matt): anything between the camera and the player
// gets a dithered hole punched through it, so walking behind a wall, a tree, or a bridge
// deck never hides the character. A world-space CONE from the camera through a player-sized
// disc at the player's depth: fragments whose view rays strike that disc, and which are
// nearer than the player, are dither-discarded with a soft edge. Driven by shader globals
// from HaloCutoutDriver; strength 0 (side-scroll, edit mode, no player) makes the whole
// test free-exit.
//
// Called from ForwardLit only. NEVER from ShadowCaster: the hole is a courtesy to the
// camera, not a hole in the world, and light must not leak through it. Not from DepthOnly
// either — cutting depth there emptied the depth texture pipeline-wide once (2026-08-07)
// and nothing critical reads depth anymore (the cave invert lives in the geometry shaders);
// SSAO seeing ghost depth inside a halo hole is a gray-box shrug.
#ifndef PROPHECY_HALO_CUTOUT_INCLUDED
#define PROPHECY_HALO_CUTOUT_INCLUDED

float3 _HaloCentre;    // the player's chest, world space
float  _HaloRadius;    // metres; the soft edge lives in the outer 40%
float  _HaloStrength;  // 0..1, the driver's fade

void ApplyHaloCutout(float3 positionWS, float4 positionCS)
{
    if (_HaloStrength <= 0.001)
        return;

    float3 toPlayer = _HaloCentre - _WorldSpaceCameraPos;
    float playerDistance = length(toPlayer);
    float3 axis = toPlayer / max(playerDistance, 1e-4);

    float3 toFragment = positionWS - _WorldSpaceCameraPos;
    float along = dot(toFragment, axis);

    // Only true occluders: past the near plane's neighbourhood, and NEARER than the player
    // by a margin — the ground at their feet and the wall at their back stay whole.
    if (along < 0.5 || along > playerDistance - 0.25)
        return;

    // A CONE, not a cylinder (Matt's shape, 2026-08-07): apex at the camera, opening
    // through a player-sized disc at the player's depth. A fragment is cut only if ITS OWN
    // view ray goes on to strike that virtual player billboard — meaning it genuinely
    // screens the body. A wall beside the player never does, however close it stands to
    // the sight line; the cylinder this replaces drilled smudges through every such wall.
    // Implemented by projecting the fragment's axis offset onto the player's depth plane.
    float axisDistance = length(toFragment - axis * along);
    float atPlayerPlane = axisDistance * (playerDistance / max(along, 0.5));
    float cut = (1.0 - smoothstep(_HaloRadius * 0.7, _HaloRadius, atPlayerPlane))
                * _HaloStrength;
    if (cut <= 0.0)
        return;

    // Bayer 4x4: stable in screen space, no sorting, no blending — gray-box honest.
    static const float bayer[16] =
    {
         0.5 / 16.0,  8.5 / 16.0,  2.5 / 16.0, 10.5 / 16.0,
        12.5 / 16.0,  4.5 / 16.0, 14.5 / 16.0,  6.5 / 16.0,
         3.5 / 16.0, 11.5 / 16.0,  1.5 / 16.0,  9.5 / 16.0,
        15.5 / 16.0,  7.5 / 16.0,  5.5 / 16.0, 13.5 / 16.0,
    };
    uint2 pixel = (uint2)positionCS.xy & 3;
    if (cut > bayer[pixel.y * 4 + pixel.x])
        discard;
}

#endif
