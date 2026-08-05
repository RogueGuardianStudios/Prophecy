# Tile UV guide — GENERATED, do not hand-edit

Written by `Prophecy > Build > Generate Overworld Tiles` (`OverworldTileBuilder`). Regenerate after any geometry change.

Each tile's UVs fill its own 0–1 square; its guide PNG (`Assets/_Prophecy/Art/Tiles/UVGuides/Tile_<Id>_UV.png`, 1024²) shows the islands colour-coded by the legend below, and ships as the tile material's base map. **To reskin a tile:** paint (or generate) a texture over its guide and point `Tile_<Id>.mat`'s base map at it — regenerating never overwrites a base map that lives outside the UVGuides folder. If texturing goes triplanar instead, swap the shader on the materials; the UVs go dormant, not wrong.

UV rects are (u, v) with v UP — image editors put pixel y down, so the pixel row is `1024 − v·1024`. Same colour = same meaning on every tile:

| Region | Colour | Meaning |
|---|---|---|
| Top | `#6BA661` | walkable surface — caps, treads, the ramp grade |
| Face | `#8C8C94` | vertical rock — cliff fronts, wedge sides, stair columns |
| Rim | `#C79E6B` | the ALttP lip along a cliff top edge |
| Skirt | `#594738` | buried / underwater continuation below the footline |
| Riser | `#A8A399` | stair fronts |
| Bank | `#D4BD85` | shore slope |
| Trim | `#404048` | hidden or mating surfaces — backs, bottoms, flat cut ends |
| Water | `#598CBF` | the sea plane |

---

## Tile_GRD_Cap — ground cap, flat 1×1 m slab

Placed at every Ground land cell, top at level × 0.7. One cap serves all levels.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Top | (0.01, 0.26 → 0.99, 0.99) | (10, 266) → (1014, 1014) | walk surface, 1.0 × 1.0 m — the region biome materials repaint |
| Face | (0.01, 0.14 → 0.99, 0.24) | (10, 143) → (1014, 246) | four slab edges — hidden between caps, but a bridge deck's edge and a cave lintel wear them in the open |
| Face | (0.01, 0.01 → 0.99, 0.12) | (10, 10) → (1014, 123) | underside — visible beneath bridge decks and overhangs |

## Tile_RMP_Ramp — ramp run cell, solid wedge rising 0 → 1 m along +Z

One cell of a 2-cell climb; the planner chains them. Sides are the closed cheeks of register #8.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Top | (0.01, 0.44 → 0.99, 0.99) | (10, 451) → (1014, 1014) | the grade, 1.0 m wide |
| Face | (0.01, 0.12 → 0.99, 0.42) | (10, 123) → (1014, 430) | closed wedge sides, one cell each (left −X side, right +X side) |
| Trim | (0.01, 0.01 → 0.99, 0.10) | (10, 10) → (1014, 102) | foot edge, head face, underside (left to right) |

## Tile_RMP_Stairs — stair run cell, 8 risers of 0.125 m along +Z

The walkable level change — interchangeable with RMP_Ramp on any run cell.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Top | (0.01, 0.74 → 0.99, 0.99) | (10, 758) → (1014, 1014) | 8 treads, foot to head left to right |
| Riser | (0.01, 0.46 → 0.99, 0.71) | (10, 471) → (1014, 727) | 8 risers, foot to head left to right |
| Face | (0.01, 0.14 → 0.99, 0.43) | (10, 143) → (1014, 440) | side columns under each tread (both sides share cells, mirrored) |
| Trim | (0.01, 0.01 → 0.99, 0.12) | (10, 10) → (1014, 123) | foot edge, head face, underside (left to right) |

## Tile_WTR_Fill — water cell, flat 1×1 m plane

Sea cells; the renderer may merge all of them into one plane and probably should.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Water | (0.05, 0.05 → 0.95, 0.95) | (51, 51) → (973, 973) | the sea surface |

## Tile_CLF_Face1 — cliff face, straight, 1 step (2.0 m)

Placed on any differing cell edge after the greedy 3-split, yawed to face the low side.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.87 → 0.99, 1.00) | (10, 891) → (1014, 1019) | the lip: front, top, back substrips (top to bottom), 1.0 m long |
| Face | (0.01, 0.42 → 0.99, 0.86) | (10, 430) → (1014, 881) | the rock face, 1.0 × 2.0 m — the main paint surface |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation, 1.0 × 0.5 m below the footline |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | back wall, cut ends, underside, rim ends (left to right) |

## Tile_CLF_Face2 — cliff face, straight, 2 steps (4.0 m)

Placed on any differing cell edge after the greedy 3-split, yawed to face the low side.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.87 → 0.99, 1.00) | (10, 891) → (1014, 1019) | the lip: front, top, back substrips (top to bottom), 1.0 m long |
| Face | (0.01, 0.42 → 0.99, 0.86) | (10, 430) → (1014, 881) | the rock face, 1.0 × 4.0 m — the main paint surface |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation, 1.0 × 0.5 m below the footline |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | back wall, cut ends, underside, rim ends (left to right) |

## Tile_CLF_Face3 — cliff face, straight, 3 steps (6.0 m)

Placed on any differing cell edge after the greedy 3-split, yawed to face the low side.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.87 → 0.99, 1.00) | (10, 891) → (1014, 1019) | the lip: front, top, back substrips (top to bottom), 1.0 m long |
| Face | (0.01, 0.42 → 0.99, 0.86) | (10, 430) → (1014, 881) | the rock face, 1.0 × 6.0 m — the main paint surface |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation, 1.0 × 0.5 m below the footline |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | back wall, cut ends, underside, rim ends (left to right) |

## Tile_CLF_OuterPost1 — cliff outer (convex) corner post, 1 step

Band rule, one high cell at the corner. Also placed back-to-back in pairs for diagonal contacts.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.85 → 0.99, 1.00) | (10, 865) → (1014, 1019) | lip fronts (−Z then −X) on the top strip, lip top cap on the square below |
| Face | (0.01, 0.42 → 0.99, 0.82) | (10, 430) → (1014, 840) | the two rock faces, 0.40 × 2.0 m each (−Z face left, −X face right) |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation of both faces, 0.5 m |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | flat back planes (+Z, +X), underside, rim backs and ends |

## Tile_CLF_OuterPost2 — cliff outer (convex) corner post, 2 steps

Band rule, one high cell at the corner. Also placed back-to-back in pairs for diagonal contacts.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.85 → 0.99, 1.00) | (10, 865) → (1014, 1019) | lip fronts (−Z then −X) on the top strip, lip top cap on the square below |
| Face | (0.01, 0.42 → 0.99, 0.82) | (10, 430) → (1014, 840) | the two rock faces, 0.40 × 4.0 m each (−Z face left, −X face right) |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation of both faces, 0.5 m |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | flat back planes (+Z, +X), underside, rim backs and ends |

## Tile_CLF_OuterPost3 — cliff outer (convex) corner post, 3 steps

Band rule, one high cell at the corner. Also placed back-to-back in pairs for diagonal contacts.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.85 → 0.99, 1.00) | (10, 865) → (1014, 1019) | lip fronts (−Z then −X) on the top strip, lip top cap on the square below |
| Face | (0.01, 0.42 → 0.99, 0.82) | (10, 430) → (1014, 840) | the two rock faces, 0.40 × 6.0 m each (−Z face left, −X face right) |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation of both faces, 0.5 m |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | flat back planes (+Z, +X), underside, rim backs and ends |

## Tile_CLF_InnerPost1 — cliff inner (concave) corner post, 1 step

Band rule, three high cells around a low notch. Fills the crease where two faces meet inward.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.85 → 0.99, 1.00) | (10, 865) → (1014, 1019) | lip fronts (−Z then −X) on the top strip, lip top cap on the square below |
| Face | (0.01, 0.42 → 0.99, 0.82) | (10, 430) → (1014, 840) | the two rock faces, 0.35 × 2.0 m each (−Z face left, −X face right) |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation of both faces, 0.5 m |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | flat back planes (+Z, +X), underside, rim backs and ends |

## Tile_CLF_InnerPost2 — cliff inner (concave) corner post, 2 steps

Band rule, three high cells around a low notch. Fills the crease where two faces meet inward.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.85 → 0.99, 1.00) | (10, 865) → (1014, 1019) | lip fronts (−Z then −X) on the top strip, lip top cap on the square below |
| Face | (0.01, 0.42 → 0.99, 0.82) | (10, 430) → (1014, 840) | the two rock faces, 0.35 × 4.0 m each (−Z face left, −X face right) |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation of both faces, 0.5 m |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | flat back planes (+Z, +X), underside, rim backs and ends |

## Tile_CLF_InnerPost3 — cliff inner (concave) corner post, 3 steps

Band rule, three high cells around a low notch. Fills the crease where two faces meet inward.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.85 → 0.99, 1.00) | (10, 865) → (1014, 1019) | lip fronts (−Z then −X) on the top strip, lip top cap on the square below |
| Face | (0.01, 0.42 → 0.99, 0.82) | (10, 430) → (1014, 840) | the two rock faces, 0.35 × 6.0 m each (−Z face left, −X face right) |
| Skirt | (0.01, 0.22 → 0.99, 0.40) | (10, 225) → (1014, 410) | buried continuation of both faces, 0.5 m |
| Trim | (0.01, 0.01 → 0.99, 0.20) | (10, 10) → (1014, 205) | flat back planes (+Z, +X), underside, rim backs and ends |

## Tile_CLF_Cheek — notch cutting wall, right triangle, 1 m tall end at −X

Flanks the run's TOP cell where it notches into the high terrace. One wall per chirality — a baked pair, not a mirror scale.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.87 → 0.99, 1.00) | (10, 891) → (1014, 1019) | the lip along the horizontal top edge |
| Face | (0.01, 0.45 → 0.99, 0.85) | (10, 461) → (1014, 870) | the triangular face above the diagonal |
| Skirt | (0.01, 0.24 → 0.99, 0.43) | (10, 246) → (1014, 440) | the 0.2 m strip buried under the ramp surface, following the diagonal |
| Trim | (0.01, 0.01 → 0.99, 0.22) | (10, 10) → (1014, 225) | back, both cut ends, sloped underside |

## Tile_CLF_CheekM — notch cutting wall, mirrored twin, 1 m tall end at +X

Flanks the run's TOP cell where it notches into the high terrace. One wall per chirality — a baked pair, not a mirror scale.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Rim | (0.01, 0.87 → 0.99, 1.00) | (10, 891) → (1014, 1019) | the lip along the horizontal top edge |
| Face | (0.01, 0.45 → 0.99, 0.85) | (10, 461) → (1014, 870) | the triangular face above the diagonal |
| Skirt | (0.01, 0.24 → 0.99, 0.43) | (10, 246) → (1014, 440) | the 0.2 m strip buried under the ramp surface, following the diagonal |
| Trim | (0.01, 0.01 → 0.99, 0.22) | (10, 10) → (1014, 225) | back, both cut ends, sloped underside |

## Tile_SHR_Edge — shoreline bank, straight, 45° drop past the waterline

Level-0 land against sea. 45°, not gentler, so the NavMesh bake can never walk onto it.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Bank | (0.01, 0.55 → 0.99, 0.99) | (10, 563) → (1014, 1014) | the bank, 1.0 m wide, dropping 0.4 m over 0.4 m — waterline (−0.35) crosses near its foot |
| Skirt | (0.01, 0.30 → 0.99, 0.52) | (10, 307) → (1014, 532) | underwater continuation down to −0.9 |
| Trim | (0.01, 0.01 → 0.99, 0.27) | (10, 10) → (1014, 276) | back wall, underside, both cut ends (left to right) |

## Tile_SHR_OuterPost — cape, shore outside corner — 45° chamfer

One level-0 land cell at the corner. The chamfer's cut sides mate with the adjoining shore edges' end planes.

| Region | UV rect (u0, v0 → u1, v1) | Pixels @1024 | What maps there |
|---|---|---|---|
| Bank | (0.01, 0.55 → 0.99, 0.99) | (10, 563) → (1014, 1014) | the chamfer triangle turning the coast 90° in one facet |
| Skirt | (0.01, 0.30 → 0.99, 0.52) | (10, 307) → (1014, 532) | underwater continuation down to −0.9 |
