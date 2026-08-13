# Meshy 6 prompt sheet — overworld tile set

> **DEPRECATED 2026-08-04 (evening).** The Meshy path for tile GEOMETRY was retired the same
> day it was written — the meshes are generated procedurally instead (`Prophecy > Build >
> Generate Overworld Tiles`; see the revision note in `Overworld-Tile-Set.md` and the UV layout
> in `Tile-UV-Guide.md`). Kept because the descriptions may still be useful as prompts for
> generating *textures* over the tiles' UV guides.

Copy-paste generation prompts only. The full spec (dimensions, pivots, placement rules, import
checks) is `Plans/Overworld-Tile-Set.md` — check each piece against it before accepting.

**Revised 2026-08-04:** explicit dimensions added to every prompt after the first
un-dimensioned generations read wrong.

**How to use:**

1. Generate **CLF_Face1 first** and iterate it until the look is right — it is the master style
   tile for the whole set.
2. The first sentence of every prompt is the frozen style block. Once Face1 is approved, never
   reword it.
3. If this Meshy build supports image conditioning / style reference, feed the approved Face1
   render into every later generation.
4. The dimensions in the prompts pin the **proportions** — Meshy's absolute scale is still
   approximate, and the import step rescales every piece to exact metres regardless. What must
   survive generation is the ratio, not the number.
5. Generate in the order below.

---

## 1. CLF_Face1 — cliff face, 1 step (MASTER — iterate this one first)

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A rectangular section of low cliff wall, 1 meter wide and 0.7 meters tall, wider than it is tall. The front shows bold horizontal rock strata leaning slightly back, with a low raised rocky rim lip about 0.1 meters high running along the whole top edge, and a plain buried skirt continuing 0.5 meters below the bottom, so the whole block is about 1 meter wide, 1.3 meters tall and 0.4 meters deep. Left and right ends are perfectly flat vertical cut planes so copies line up side by side in a row. The back is a plain flat wall.
```

## 2. CLF_Face2 — cliff face, 2 steps

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A rectangular section of cliff wall, 1 meter wide and 1.4 meters tall, taller than it is wide. The front shows bold horizontal rock strata leaning slightly back, with a low raised rocky rim lip about 0.1 meters high running along the whole top edge, and a plain buried skirt continuing 0.5 meters below the bottom, so the whole block is about 1 meter wide, 2 meters tall and 0.4 meters deep. Left and right ends are perfectly flat vertical cut planes so copies line up side by side in a row. The back is a plain flat wall.
```

## 3. CLF_Face3 — cliff face, 3 steps

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A tall sheer section of cliff wall, 1 meter wide and 2.1 meters tall, about twice as tall as it is wide. The front shows bold stacked horizontal rock strata leaning slightly back, with a low raised rocky rim lip about 0.1 meters high running along the whole top edge, and a plain buried skirt continuing 0.5 meters below the bottom, so the whole block is about 1 meter wide, 2.7 meters tall and 0.4 meters deep. Left and right ends are perfectly flat vertical cut planes so copies line up side by side in a row. The back is a plain flat wall.
```

## 4. CLF_OuterPost1 — convex cliff corner, 1 step

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A vertical corner post of rock where two low cliff walls meet at an outside right angle, like the rocky nose of a plateau corner seen from above. It is 0.7 meters tall and about 0.6 meters wide and 0.6 meters deep, short and squat, with a raised rocky rim lip about 0.1 meters high wrapping around the top corner and a plain buried skirt continuing 0.5 meters below the base. The two inner sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 5. CLF_OuterPost2 — convex cliff corner, 2 steps

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A vertical corner post of rock where two cliff walls meet at an outside right angle, like the rocky nose of a plateau corner seen from above. It is 1.4 meters tall and about 0.6 meters wide and 0.6 meters deep, about twice as tall as it is wide, with a raised rocky rim lip about 0.1 meters high wrapping around the top corner and a plain buried skirt continuing 0.5 meters below the base. The two inner sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 6. CLF_OuterPost3 — convex cliff corner, 3 steps

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A vertical corner post of rock where two tall cliff walls meet at an outside right angle, like the rocky nose of a plateau corner seen from above. It is a tall narrow column, 2.1 meters tall and about 0.6 meters wide and 0.6 meters deep, more than three times as tall as it is wide, with a raised rocky rim lip about 0.1 meters high wrapping around the top corner and a plain buried skirt continuing 0.5 meters below the base. The two inner sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 7. CLF_InnerPost1 — concave cliff corner, 1 step

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A vertical inside-corner wedge of rock where two low cliff walls meet at an inside right angle, like the concave corner of a canyon. It is 0.7 meters tall and about 0.6 meters wide and 0.6 meters deep, short and squat, with a raised rocky rim lip about 0.1 meters high running through the top of the corner and a plain buried skirt continuing 0.5 meters below the base. The two outer sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 8. CLF_InnerPost2 — concave cliff corner, 2 steps

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A vertical inside-corner wedge of rock where two cliff walls meet at an inside right angle, like the concave corner of a canyon. It is 1.4 meters tall and about 0.6 meters wide and 0.6 meters deep, about twice as tall as it is wide, with a raised rocky rim lip about 0.1 meters high running through the top of the corner and a plain buried skirt continuing 0.5 meters below the base. The two outer sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 9. CLF_InnerPost3 — concave cliff corner, 3 steps

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A vertical inside-corner wedge of rock where two tall cliff walls meet at an inside right angle, like the concave corner of a canyon. It is tall and narrow, 2.1 meters tall and about 0.6 meters wide and 0.6 meters deep, more than three times as tall as it is wide, with a raised rocky rim lip about 0.1 meters high running through the top of the corner and a plain buried skirt continuing 0.5 meters below the base. The two outer sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 10. RMP_Ramp — ramp cell

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A solid square wedge of natural stone forming an even ramp, 1 meter by 1 meter in footprint, rising smoothly from ground level at one edge to 0.7 meters high at the opposite edge — a 35 degree slope, like a rock slope connecting two terraces. The sloped top is a clean even grade with slightly softened top and bottom edges. The two side walls are closed and show the triangular wedge profile as flat stone; all four outer faces end in perfectly flat vertical planes. No overhangs.
```

## 11. RMP_Stairs — stair cell

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A solid square block of carved stone stairs, 1 meter by 1 meter in footprint: exactly four wide shallow steps, each about 0.18 meters high and 0.25 meters deep, rising from ground level at one edge to 0.7 meters high at the opposite edge, each step spanning the full 1 meter width, like a staircase cut into bedrock. The two side walls are closed and show the stepped profile; all four outer faces end in perfectly flat vertical planes. No railings, no overhangs.
```

## 12. CLF_Cheek — ramp cutting wall

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A right-triangle retaining wall of layered rock, like the side wall of a staircase cut into a low cliff: a wedge-shaped wall panel 1 meter long and about 0.3 meters thick, 0.7 meters tall at one end and tapering along a straight diagonal down to nothing at the other end, with a raised rocky rim lip about 0.1 meters high along the horizontal top edge and a thin buried skirt about 0.2 meters below the diagonal. Both ends are perfectly flat vertical cut planes. The back is a plain flat wall.
```

## 13. SHR_Edge — shoreline bank, straight

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A straight section of rocky shoreline bank: a strip 1 meter wide and about 0.5 meters deep, a steep stony bank dropping about 0.9 meters from a perfectly straight flat top edge down past the waterline, with a few rounded stones at the waterline, like the coast edge of a top-down island map. Both ends are perfectly flat vertical cut planes so copies continue the coastline in a row. The back is a plain flat wall.
```

## 14. SHR_OuterPost — cape (shore convex corner)

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A small rocky point where two straight shoreline banks meet at an outside right angle, like a miniature cape on a top-down island map: a steep stony corner nose about 0.6 meters wide and 0.6 meters deep, wrapping around the turn and dropping about 0.9 meters past the waterline, with a couple of rounded stones at its foot. The two inner sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 15. SHR_InnerPost — bay notch (shore concave corner)

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A small rocky inside corner where two straight shoreline banks meet at an inside right angle, like a tiny bay notch on a top-down island map: a steep stony bank about 0.6 meters wide and 0.6 meters deep, filling the concave turn and dropping about 0.9 meters past the waterline. The two outer sides are perfectly flat vertical planes meeting at ninety degrees.
```

## 16. GRD_Cap — ground cap (OPTIONAL — skip if it won't tile cleanly; a procedural quad is fine)

```
Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets, clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game asset, hard edges, cel-shading friendly. A perfectly square flat slab of stony ground, 1 meter by 1 meter and only 0.15 meters thick, the top surface almost completely flat with only the faintest large facets, all four edges perfectly straight and level so identical copies tile side by side seamlessly in a grid. No rocks, no props, no relief at the edges.
```

---

*(No prompt for `WTR_Fill` — the water is a procedural flat plane, nothing to generate.)*
