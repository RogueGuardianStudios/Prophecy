# Overworld Tile Set — the gray-box catalogue and placement contract

> **RECESSED, 2026-08-05:** the run's top `RampRecess = RampRun / 2` cells are carved OUT of
> the high terrace (the ALttP inset stair — one tile at the bottom, one in the wall), with the
> head landing on high ground beyond the notch. The cheek is a ONE-cell wall flanking the
> notch, one rise tall, and the jamb posts at the notch mouth fall out of the band rule.

> **RUNS, same night:** a climb now spans `OverworldTileGrid.RampRun = 2` cells — one cell per
> 3 m step was a ladder. Ramp cells carry a run index (0 at the foot); each ramp/stair piece
> rises Step/RampRun (stairs: 8 risers of 0.1875 m, so a full climb is a 16-riser staircase);
> the cheek is one run-long wall; and run pieces carry a **RunSkirt** — sides and underside
> reach (RampRun−1)·rise below their own foot plane, because upper run cells float above the
> low terrace and open flanks read as holes. Connectivity is exact fixed-point in 1/RampRun
> level units. The compiler requires the whole run to fit on flat ground behind a boundary.

> **LAYERS, 2026-08-05:** cells may carry one OVERLAY surface (`OverworldMap.Layers` — a rect
> at a Y: below the terrain a cave floor, above it a bridge deck or overhang; over sea, a
> bridge over water). No new pieces: the overlay is a Cap (its sides and underside now wear the
> rock colour — deck edges and cave lintels put them in the open), cave interior walls are
> ordinary faces from floor to rock top, and a cave MOUTH is an opening: a connection that
> pierces a wall band suppresses that wall, while a deck meeting a terrace at the band's top
> keeps it. Which surface a body walks is resolved by edge-height connectivity and carried in
> the sim's opaque `GroundLayer` token.

> **Step settled at 2.0 m, 2026-08-05** — 0.7 was a kerb, 3.0 was a fortress; 2, just over the
> player's 1.8, reads as a wall without dwarfing the map. Everything below derives from
> `OverworldTileGrid.Step`; the 3.0-era note is kept as history.

> **RESCALED 2026-08-04 (late): the terrace step is 3.0 m, not 0.7** — Matt's call after walking
> it: a 0.7 ledge read knee-high against the 1.8 m player; a terrace should be a WALL that
> towers. `OverworldTileGrid.Step` is the single source of truth and the tile generator derives
> from it. Consequences: faces are 3/6/9 m; **stairs have 8 risers of 0.375 m** (each under
> agentClimb 0.45, so staircases route on the NavMesh); the smooth ramp piece rises the full
> step over one cell — 72° — and is cosmetic until multi-cell ramp runs exist (stairs are the
> host default); **sea level is a world constant −0.35, deliberately decoupled from the step**
> (the shore pieces are sculpted around the waterline); rims scaled to 0.2 (posts 0.24); and
> every cut plane that a height transition can expose (face ends, post backs, cheek ends, rim
> ends) wears the rock or lip colour rather than trim darkness. The authored map was migrated
> in place (levels 0–4 → Y 0/3/6/9/12; East Mesa settled at level 1, which makes its trail
> compile). Dimension figures below that derive from the old 0.7 step are historical; the
> grammar, placement rules and conventions all stand.

**Written 2026-08-04.** This is the tile list for the 3D tile structure (HANDOFF.md, "The 3D tile
structure — agreed direction"): what pieces exist, exactly how each is shaped, and the Meshy AI
prompt that generates it. It doubles as the **placement contract** — the renderer that consumes
these tiles does not exist yet, and when it is built it is built against this document.

> **REVISED again, same evening — the renderer is live and three pieces changed after first
> contact:** the cheek's mirror is now a **baked twin piece `CLF_CheekM`** (negative scale
> reverses winding and rendered the far wall of every cutting inside-out); the shore cape
> (`SHR_OuterPost`) is now a **single 45° chamfer wedge** whose straight sides mate with the
> adjoining shore edges' end cut planes (the mitred version was coplanar with both banks and
> z-fought them, and its diagonal corner jutted a stray triangle into the sea); and
> **`SHR_InnerPost` is CUT** — two banks meeting inward already intersect in the correct valley
> along the corner diagonal, so any patch over them is coplanar by construction. Post rims now
> stand 0.12 m (0.02 proud of face rims) so overlapping rim tops never share a plane, and rims
> gained an underside strip on the proud lip. Still **17 pieces**. The placement contract below
> is implemented by `TilePiecePlanner` (`Runtime/Overworld/OverworldTilePlan.cs`), pinned by
> `OverworldTileGridTests`.

> **REVISED 2026-08-04 (evening): the Meshy path for GEOMETRY is retired** — the first
> generations didn't hold the contract. The meshes are now **generated procedurally** by
> `Prophecy > Build > Generate Overworld Tiles` (`OverworldTileBuilder` /
> `OverworldTileGeometry`, `Packages/com.rokkan.prophecy/Editor/Build/`), which emits a mesh,
> prefab, material and colour-coded UV guide per tile plus `Plans/Tile-UV-Guide.md`. Reskinning
> is now **textures painted over the generated UVs** (each material's base map starts as its
> tile's guide PNG; regenerating never overwrites a base map pointing outside `UVGuides/`) — or
> a **triplanar shader** later, which the hard-edged axis-aligned geometry suits; the UVs then
> go dormant, not wrong. Everything below about shapes, dimensions, placement rules and
> conventions **still binds** — the generator implements it. §5's prompts and §6's Meshy
> workflow are kept as history, and `Meshy-Tile-Prompts.md` may still serve as texture-prompt
> fodder.

These tiles are **not** for the Stålberg placer (`StalbergTilePlacer` and its six
marching-squares shapes stay with the worldgen package for the side-scroll pipeline). This is a
fresh grammar for the discrete-level grid.

Decisions fixed with Matt 2026-08-04: stylized-neutral look (no biome identity — Design Bible
§5.3, "geometry stays, it darkens"); **both** ramp and stairs; cliff faces in **1/2/3-step
height variants**; **water + shore** pieces so coasts read as coast.

---

## 1. Purpose and scope

- **In scope:** the complete enumeration of tile pieces, the geometric contract each must obey,
  the per-family placement rule, and a copy-paste Meshy prompt per piece.
- **Out of scope:** the renderer, the `OverworldMap` → `level[x,z]` compiler, and the
  `OverworldTileInstaller` (all named here so the contract points at them, all later sessions).
- **The art is placeholder-quality on purpose** — good enough to make the ALttP grammar legible,
  neutral enough that biomes and the Consumed state arrive as material swaps with the geometry
  untouched.

## 2. The grid contract

Constants, verified against source 2026-08-04:

| Constant | Value | Source |
|---|---|---|
| Tile footprint | **1 × 1 m** | `OverworldMap.Spacing` (asset, Rectangular topology) |
| Terrace step | **0.7 m** | authored Y values 0/0.7/1.4/2.1/2.8; `AuthoredRamp.EndY` default |
| Levels in play | **0..4** (max span 2.8 m) | `OverworldMap.asset` (East Mesa's 1.1 will quantize) |
| Water plane | **Y = −0.35** (half a step below level 0) | this doc — a bank, not a cliff |
| Player | StandHeight 1.8, BodyWidth 0.9 | `MovementTuningData` |
| NavMesh bake today | slope 25°, climb 0.45, radius 0.35 | `OverworldGridHost` |

The data model the tiles render: `level[x,z]` integer elevation per cell, plus a per-cell kind —
**Ground**, or **Ramp with a facing** (joins level N to N+1 along its axis, one step per cell;
longer climbs are runs of consecutive ramp cells). Sea is any cell outside the land region,
treated as **level 0 land-adjacent water** for edge analysis. Cliff faces are not data; they are
placed wherever two neighbours differ.

### Placement rule per family

- **`GRD_Cap`** at every Ground land cell, positioned `(x + 0.5, level × 0.7, z + 0.5)`.
- **`RMP_Ramp` / `RMP_Stairs`** replace the cap on Ramp cells, yawed to the authored facing
  (ascending toward it). Which of the two is used is chosen per location by the author.
- **`CLF_FaceN`** on every cell edge where the two levels differ (sea counts as level 0 against
  land ≥ 1): total drop ΔL splits **greedily top-down into 3s** (ΔL 4 → a 3-step piece on top,
  a 1-step at the base — the tall uninterrupted stratum sits at plateau eye level, the short
  piece reads as a talus band at the foot). Positioned at the edge midpoint at the **footline**
  (the segment's lower level), yawed so the front faces the low side.
- **`CLF_OuterPostN` / `CLF_InnerPostN`** from the **band rule** (§2a) at cell corners.
- **`CLF_Cheek`** on the high side of any edge between a Ramp cell and a neighbour at ≥ N+1 —
  the retaining wall of a cutting. The two walls of one cutting are **mirror images**: place one
  instance with `localScale.x = −1`. This is the only piece with chirality; every other piece is
  authored so 90° yaws suffice. (Fallback if negative scale ever causes lighting artifacts:
  export a second mirrored FBX.)
- **`SHR_Edge` / `SHR_OuterPost` / `SHR_InnerPost`** on edges/corners between **level-0 land**
  and sea — same case analysis as cliffs with "high" → "land", one band only.
- **`WTR_Fill`** at sea cells (the renderer may merge all sea into one plane).

### 2a. The band rule (corners)

At each interior grid corner four cells meet. Sort their levels into consecutive **bands**; in
each band the 2×2 high/low occupancy is exactly one of four cases:

| Occupancy in the band | Piece |
|---|---|
| 2 adjacent high | none — the collinear faces butt on their flat side planes |
| 1 high | `CLF_OuterPost` (convex nose) |
| 3 high | `CLF_InnerPost` (concave fill) |
| 2 diagonal high | **two `CLF_OuterPost` back to back** on the same corner line (see register #2) |

Consecutive bands with the same occupancy merge and take the greedy 1/2/3 height split, exactly
like faces.

**Worked example** — corner cells NW=3, NE=1, SE=0, SW=0. Band 0→1 has {NW, NE} high: two
adjacent → no post; the NE–SE edge's `Face1` and the bottom band of the NW–SW edge's `Face3`
butt flat. Bands 1→2 and 2→3 both have only {NW} high: merged → one **2-step outer post** with
its foot at level 1 on the corner line. Faces split per-edge, posts per-corner — the two splits
need not align, and don't need to: overlap hides the difference (§2b).

### 2b. Seams hide by baked overlap, never by precision

Meshy output is organic; mating will never be machine-perfect. So the contract is:

- Every piece with a bottom edge bakes a **0.5 m under-skirt** below its nominal footline —
  buried behind the lower cap, or drowned below the water plane.
- Every cliff/shore piece with a top edge bakes a **raised rim**, ≤ 0.12 m high, overlapping
  ≤ 0.2 m onto the high cap. The rim is also the ALttP cliff-lip read — the grammar made
  visible, not just a seam patch.
- **Side ends of every edge and corner piece are flat vertical cut planes** on the cell-corner
  planes; sculpt relief never crosses them, so runs tile and posts cover the turns.
- Rim overlap budget (≤ 0.2 m per side) is chosen so a 1-cell-wide ridge keeps ≥ 0.6 m of
  clear cap between opposing rims.

### 2c. NavMesh consequences (for the renderer session — stated here, built later)

- The bake's slope limit must rise **25° → ~40°**: a one-cell ramp is a 35° grade and must be
  walkable. Safety holds because every non-walkable surface in this set is authored **≥ 60°**
  (cliffs) or **≥ 45°** (shore banks) — both beyond the limit.
- **Recommended stronger option:** bake from **caps + ramp/stair tops only**, excluding
  faces/posts/shore from the input meshes. Adjacent caps 0.7 m apart never merge under
  agentClimb 0.45, so cliffs cannot be climbed even in principle, and the shore-bank angle
  stops mattering.
- Stair risers are 4 × 0.175 m — under agentClimb 0.45, so the bake reads stairs as a slope.

## 3. Enumeration and completeness

**Three families, one per boundary feature.** The rendered world is a union of axis-aligned
prisms (one per cell, height `level × 0.7`). The boundary of such a union consists of exactly:
(a) horizontal cell-top rectangles, (b) vertical rectangles on shared cell edges, and (c)
vertical lines at cell corners. Cell pieces cover (a), edge pieces cover (b), corner posts cover
(c) — so **every authorable map is covered by construction**, and no U-piece, T-piece, or
combinatorial corner tile can ever be needed.

### The register: cases considered and resolved without new tiles

| # | Case | Resolution |
|---|---|---|
| 1 | 4-step cliff (2.8 m, current max) | greedy 3+1 stack; generalizes 3+3+…+remainder |
| 2 | Diagonal contact (checkerboard highs) | two back-to-back outer posts; **authoring lint** — the compiler should warn, ALttP grammar avoids the pinch and 4-neighbour walkability already says diagonals don't connect |
| 3 | 1-cell-wide ridge | faces both sides; rim budget keeps 0.6 m clear cap |
| 4 | 1-cell notch/chasm | opposed faces + inner posts; author routes ≥ 2 cells wide for the player (BodyWidth 0.9) |
| 5 | Promontory / free-standing pillar | 3 or 4 faces + outer posts |
| 6 | Cliff meets water directly (land ≥ 1 beside sea) | stack faces as if sea were level 0; the bottom skirt reaches −0.5, below the −0.35 waterline. No shore piece — rock plunging into sea *is* the cliff-coast read |
| 7 | Shore run meets cliff coast | flat side planes butt; the cliff piece's skirt covers the junction underwater |
| 8 | Ramp side over lower ground (side neighbour < N) | the wedge's own closed side covers N..N+1; standard faces stack below it |
| 9 | Ramp cut through a cliff (side neighbour ≥ N+1 — the classic ALttP inset stair) | `CLF_Cheek` + mirrored instance; standard faces above if the wall is taller |
| 10 | Ramp head into a taller wall / foot at a drop edge | the wedge's flat end faces + standard faces above/below |
| 11 | Multi-step climbs | runs of consecutive ramp cells; flush wedge-to-wedge joints |
| 12 | Side-by-side parallel ramps | cheeks hidden inside each other; harmless overdraw |
| 13 | Ramp top/bottom lip transition tiles | cut — a ~0.05 m chamfer arris is baked into the wedge instead; mating edges stay flat |
| 14 | Cap edge-trim tiles | cut — the rim is baked into faces and posts |
| 15 | Ramp foot at the waterline | ramp cells are level-0 land for shore purposes; `SHR_Edge` places under its exposed edges as usual |
| 16 | Rivers / waterfalls | **deferred** — no river data exists (`RegionOrigin.River` is reserved, unpopulated) |

## 4. Authoring conventions (binding on every piece)

- **Pivots.** Cell pieces: centre of the 1×1 footprint, **Y at the cell's walk level** (cap: the
  top surface; ramp/stairs: the foot plane). Edge pieces: midpoint of the 1 m edge, Y at the
  footline. Corner posts: on the corner line, Y at the band foot.
- **Canonical facing.** Edge pieces front toward **−Z**. Ramp/stairs ascend toward **+Z**.
  Posts authored with the high (or land) quadrant at **+X+Z**. The renderer rotates instances
  in 90° yaw steps; only `CLF_Cheek` may mirror.
- **Mating dimensions are exact after import** (the installer normalizes them — §6): 1.00 m
  widths on edge pieces, 1×1 m cell footprints, N × 0.7 m nominal heights. Overlap budgets:
  rim ≤ 0.12 high / ≤ 0.2 onto the cap, under-skirt 0.5, face relief bulge ≤ 0.25 into the low
  cell, post radius ≤ 0.3.
- **Flat things are flat.** Side cut planes perfectly vertical and flush to the cell-corner
  planes; cap tops within ±0.05 of the walk plane, cap **edges dead straight at exactly walk
  level** — caps butt cap-to-cap with no covering piece, which is why the cap is the piece most
  likely to stay procedural (§5, `GRD_Cap`).
- **Steepness.** Ramp top exactly 35° (0.7 over 1.0). Stair risers 4 × 0.175, treads 0.25.
  Cliff and cheek surfaces **≥ 60° from horizontal** everywhere — the placeholder set's lesson
  (45° skirts NavMesh-climbed) applied as a hard authoring rule. Shore banks **45–50°**: softer
  than cliff, still above the 40° bake limit so the AI can never route onto a beach.
- **Polycount 300–1,500 tris** per piece, flat-shaded hard normals, clean silhouette. ~6,900
  cells at 96×72 m is trivial under instancing at this budget.
- **Exactly one material slot, neutral light gray, no baked texture identity.** This is the
  rule that keeps biome sets and the Consumed state a material swap. (The overworld host
  currently overwrites tile materials with one shared ground material anyway — authored
  materials would be discarded.)
- **No two pieces may place same-facing surfaces on one cell-boundary plane** (added 2026-08-04
  after the boiling-stipple hunt): the piece that COVERS an edge owns its plane alone. Cap sides
  inset 0.02 m inside the boundary; stacked faces/posts step 0.01 m toward the low side per
  tier; post rims ride 0.02 m proud of face rims. A view-dependent stipple that survives
  disabling shadows is coplanar geometry, not lighting.

## 5. Tile catalogue

**Prompt text lives in `Plans/Meshy-Tile-Prompts.md`** — one fenced copy-paste block per
generation, in generation order, **with explicit dimensions** (revised 2026-08-04: the first
un-dimensioned generations read wrong, so the sheet is the single authoritative prompt source
and this catalogue keeps the shape contract, specs, placement rules and checks).

Every prompt begins with the frozen **STYLE BLOCK**, verbatim:

> *"Stylized low-poly rock terrain tile for a video game, chunky angular stone with flat facets,
> clean silhouette, neutral light gray, untextured, no grass or moss or color, modular game
> asset, hard edges, cel-shading friendly."*

Prompts state **proportions, not metres** — Meshy's scale is approximate and the installer
rescales by bounds. Generate **`CLF_Face1` first** as the master style tile, iterate until the
look is approved, then keep the STYLE BLOCK frozen for everything else (and if this Meshy 6
build supports image conditioning, feed the approved master's render into later generations).

**Order:** Face1 → Face2/3 → posts → ramp/stairs → cheek → shore → cap.

---

### CLF_Face1 — cliff face, straight, 1 step  *(master style tile)*

- **Shape:** a straight run of cliff wall. Front shows bold horizontal strata leaning back
  ≤ 15° from vertical, relief bulging ≤ 0.25 m into the low cell; a low rocky rim lip along the
  whole top edge; a plain skirt continuing 0.5 m below the footline. Back is a plain flat wall.
- **Spec:** nominal 1.00 W × 0.70 H · envelope ~1.00 × 1.32 H × 0.45 D · pivot edge-mid at
  footline · front −Z · all faces ≥ 60°.
- **Placement:** any 1-step band of a differing edge, after the greedy split; yawed to face the
  low side.
- **Prompt:** `Meshy-Tile-Prompts.md` §1.
- **Checks:** flat side planes · ≥ 60° everywhere · rim present · single material · ≤ 1,500 tris.

### CLF_Face2 — cliff face, straight, 2 steps

- **Shape:** as Face1, taller; strata read as two bands without a horizontal seam.
- **Spec:** nominal 1.00 W × 1.40 H · envelope ~1.00 × 2.02 H × 0.45 D · pivot edge-mid at
  footline · front −Z.
- **Placement:** 2-step bands after the greedy split.
- **Prompt:** `Meshy-Tile-Prompts.md` §2.
- **Checks:** as Face1.

### CLF_Face3 — cliff face, straight, 3 steps

- **Shape:** as Face1, sheer; three strata bands, one unbroken silhouette.
- **Spec:** nominal 1.00 W × 2.10 H · envelope ~1.00 × 2.72 H × 0.45 D · pivot edge-mid at
  footline · front −Z.
- **Placement:** 3-step bands; the top piece of every drop ≥ 3.
- **Prompt:** `Meshy-Tile-Prompts.md` §3.
- **Checks:** as Face1.

### CLF_OuterPost1 / 2 / 3 — convex cliff corner post

- **Shape:** a vertical nose of rock on the corner line where two cliff walls meet at an outside
  right angle — bulges ≤ 0.3 m into the low quadrants, carries the rim lip around the turn,
  skirt below. The two back faces are flat vertical planes (they sit against the ends of the
  adjoining straight faces).
- **Spec:** nominal height 0.70 / 1.40 / 2.10 · radius ≤ 0.3 · pivot on the corner line at band
  foot · high quadrant +X+Z.
- **Placement:** band rule case "1 high"; also placed twice, back to back, for diagonal contacts
  (register #2).
- **Prompt:** `Meshy-Tile-Prompts.md` §§4–6 (one per height).
- **Checks:** flat back planes at 90° · rim wraps the turn · ≥ 60° everywhere.

### CLF_InnerPost1 / 2 / 3 — concave cliff corner post

- **Shape:** the mirror condition — three high cells around one low notch. A wedge of rock
  filling the inside corner where two cliff walls meet at an inside right angle, rim carried
  through the turn, skirt below.
- **Spec:** nominal height 0.70 / 1.40 / 2.10 · bulge ≤ 0.3 into the notch · pivot on the corner
  line at band foot · high quadrant +X+Z (the open low quadrant at −X−Z).
- **Placement:** band rule case "3 high".
- **Prompt:** `Meshy-Tile-Prompts.md` §§7–9 (one per height).
- **Checks:** as OuterPost.

### CLF_Cheek — ramp cutting wall  *(the one mirrored piece)*

- **Shape:** the retaining wall beside a staircase or ramp cut into a cliff: a right-triangle
  wall panel, 1 m long, 0.7 m tall at one end tapering to nothing at the other. The **bottom
  edge follows the ramp's 35° diagonal** (plus 0.2 m of skirt below it — sized to also cover the
  stair tile's zigzag side profile); the top edge is horizontal at the high level, carrying the
  rim lip. Both ends flat vertical planes.
- **Spec:** nominal 1.00 L × 0.70 max H · envelope ~1.00 × 1.02 H × 0.35 D · pivot edge-mid at
  the ramp-foot walk level · front −Z · **tall end at local −X** (mirror covers the other wall).
- **Placement:** high side of a Ramp-cell edge whose side neighbour is ≥ N+1; yawed so the front
  faces the ramp; one of the pair mirrored.
- **Prompt:** `Meshy-Tile-Prompts.md` §12.
- **Checks:** diagonal matches a 35° grade · flat ends · rim on top only · ≥ 60° front face.

### RMP_Ramp — ramp cell

- **Shape:** a solid stone wedge filling the cell: square footprint, top surface an even 35°
  grade from the foot edge to the head edge, a ~0.05 m chamfer arris at foot and head (no knife
  edge), **closed sculpted sides** showing the wedge profile, flat vertical end faces. A 0.15 m
  base slab under the foot edge.
- **Spec:** 1.00 × 1.00 footprint · rises 0 → 0.70 along +Z · pivot footprint-centre at the foot
  plane · ascends +Z.
- **Placement:** replaces the cap on a Ramp cell, yawed to the facing.
- **Prompt:** `Meshy-Tile-Prompts.md` §10.
- **Checks:** top exactly one even grade · sides closed · footprint square · flat end planes.

### RMP_Stairs — stair cell

- **Shape:** the same solid envelope as `RMP_Ramp`, top stepped instead of smooth: **four wide
  shallow risers** spanning the full width, closed sides showing the stepped profile.
- **Spec:** 1.00 × 1.00 footprint · 4 risers × 0.175 (treads 0.25) rising along +Z · pivot
  footprint-centre at the foot plane · ascends +Z.
- **Placement:** interchangeable with `RMP_Ramp` on any Ramp cell — chosen per location (stairs
  at cliff cuttings, ramps for roads).
- **Prompt:** `Meshy-Tile-Prompts.md` §11.
- **Checks:** exactly 4 risers · full-width treads · sides closed · flat end planes.

### SHR_Edge — shoreline bank, straight

- **Shape:** a straight bank strip: from a dead-straight top edge at walk level, rocky bank
  sloping down at ~45–50° past the waterline, reaching ≤ 0.5 m into the sea cell, with a plain
  skirt continuing to −0.9. A few rounded stones at the waterline sell the read. (45–50°, not
  gentler: the bank must stay above the ~40° NavMesh slope limit so nothing can walk onto it.)
- **Spec:** nominal 1.00 W · top at Y 0, skirt to −0.9 · reach ≤ 0.5 into sea · pivot edge-mid
  at walk level · front (seaward) −Z. Collision already treats coast partials as sea — art one
  tile looser than the walk grid, the safe direction.
- **Placement:** every edge between level-0 land and sea.
- **Prompt:** `Meshy-Tile-Prompts.md` §13.
- **Checks:** straight top edge · bank ≥ 45° · flat ends · reach within half a tile.

### SHR_OuterPost — cape (shore convex corner)

- **Shape:** the point where two shoreline banks meet around a land corner — a small rocky cape
  nose, bank wrapping 90° around, skirt below. Flat back planes like the cliff posts.
- **Spec:** height as `SHR_Edge` · bulge ≤ 0.3 · pivot corner line at walk level · land
  quadrant +X+Z.
- **Placement:** shore band rule, "1 land".
- **Prompt:** `Meshy-Tile-Prompts.md` §14.
- **Checks:** as SHR_Edge, plus 90° flat back planes.

### SHR_InnerPost — bay notch (shore concave corner)

- **Shape:** the inside corner where the sea notches into land — bank filling the concave turn.
- **Spec:** as `SHR_OuterPost`, open sea quadrant at −X−Z.
- **Placement:** shore band rule, "3 land".
- **Prompt:** `Meshy-Tile-Prompts.md` §15.
- **Checks:** as SHR_OuterPost.

### GRD_Cap — ground cap  *(Meshy-optional — procedural fallback is first-class)*

- **Shape:** a flat square slab of ground, 0.15 m thick, top within ±0.05 of dead flat and all
  four **edges perfectly straight at exactly walk level** — caps butt cap-to-cap with nothing
  covering the seam, so edge straightness is the whole contract. This is the piece Meshy is most
  likely to fail at; **a plain procedural quad is an accepted substitute**, and one cap serves
  every level (biomes recolour it, they never resculpt it).
- **Spec:** 1.00 × 1.00 × 0.15 slab · pivot footprint-centre at the **top** surface.
- **Placement:** every Ground land cell at `(x + 0.5, level × 0.7, z + 0.5)`.
- **Prompt:** `Meshy-Tile-Prompts.md` §16.
- **Checks:** edges straight and coplanar · top ±0.05 · tiles with itself in a 3×3 test.

### WTR_Fill — water  *(procedural — no Meshy generation)*

- **Shape:** a flat quad at Y = −0.35. Listed for contract completeness; the renderer may merge
  all sea cells into a single plane and probably should.
- **Placement:** sea cells (or one world-sized plane under everything beyond the coast).

---

## 6. Generation and import workflow

1. **Generate in Meshy 6** in the §5 order. `CLF_Face1` is the master: iterate it to an
   approved look before generating anything else; keep the STYLE BLOCK verbatim thereafter;
   use image conditioning from the approved master if available.
2. **Send to Unity via the Meshy Bridge** (`Meshy > Bridge`) — arrives under
   `Assets/MeshyImports/<name>_<timestamp>/` as FBX (textures beside it, unused here).
3. **`OverworldTileInstaller`** (future editor command, the `HeroModelInstaller` pattern):
   normalizes each import by measured renderer bounds — the **mating dimension is scaled
   exact** (1.00 m width for edge pieces, 1×1 footprint for cell pieces) and heights verified
   against a 0.7 m gauge — re-pivots per §4, strips materials to the one shared neutral
   material, and saves a prefab per id under `Assets/_Prophecy/Art/Tiles/Tile_<Id>.prefab`.
   Until it exists, the same normalization can be done by hand per §4's spec lines.
4. **Verify mating** before accepting a piece: a 3×3 of caps, a 4-run of each face with both
   posts, a ramp+cheek cutting, a shore run around one corner — seams must disappear under the
   overlap budgets at the overworld camera (pitch 50, FOV 22).

## 7. Summary table

| Id | Family | Nominal size (m) | Pivot | Facing | Source |
|---|---|---|---|---|---|
| `GRD_Cap` | cell | 1 × 1 × 0.15 | centre, top | — | Meshy *(procedural OK)* |
| `RMP_Ramp` | cell | 1 × 1, rise 0.7 | centre, foot | ascends +Z | Meshy |
| `RMP_Stairs` | cell | 1 × 1, 4 × 0.175 | centre, foot | ascends +Z | Meshy |
| `WTR_Fill` | cell | 1 × 1 at −0.35 | — | — | procedural |
| `CLF_Face1` | edge | 1 W × 0.7 H | edge-mid, foot | front −Z | Meshy |
| `CLF_Face2` | edge | 1 W × 1.4 H | edge-mid, foot | front −Z | Meshy |
| `CLF_Face3` | edge | 1 W × 2.1 H | edge-mid, foot | front −Z | Meshy |
| `CLF_OuterPost1/2/3` | corner | r ≤ 0.3 × 0.7/1.4/2.1 | corner, foot | high +X+Z | Meshy ×3 |
| `CLF_InnerPost1/2/3` | corner | r ≤ 0.3 × 0.7/1.4/2.1 | corner, foot | high +X+Z | Meshy ×3 |
| `CLF_Cheek` | edge | 1 L × 0.7 max H | edge-mid, foot | front −Z, tall −X | generated |
| `CLF_CheekM` | edge | 1 L × 0.7 max H | edge-mid, foot | front −Z, tall +X (baked twin) | generated |
| `SHR_Edge` | edge | 1 W, 0 → −0.9 | edge-mid, walk | sea −Z | generated |
| `SHR_OuterPost` | corner | 45° chamfer, reach 0.4 | corner, walk | land +X+Z | generated |

**17 pieces: 16 Meshy generations** (of which the cap may stay procedural) **+ 1 procedural.**

## 8. Deferred

- **Rivers and waterfalls** — no data (`RegionOrigin.River` reserved, unpopulated).
- **Decor/props** (trees, rocks, landmark pieces) — a separate pass with its own placement
  story; nothing structural depends on it.
- **Biome material sets and the Consumed-state swap** — the whole point of the one-material
  rule; arrives when a biome does.
- **The renderer and the `OverworldMap` → `level[x,z]` compiler** — next session's build
  (HANDOFF, "The 3D tile structure").
- **`Prophecy > Overworld > Audit Reachability`** — the reachability audit from the Stålberg
  saga is worth enshrining as a menu item when the tile build lands (HANDOFF, "Current live
  state").
