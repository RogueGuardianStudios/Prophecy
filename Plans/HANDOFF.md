# Prophecy — session handoff

**Written:** 2026-07-30, refreshed 2026-08-03 (evening). The session that built M4's combat spine,
made it scale, gave it a body and a stat sheet, gave it something to fight — and then opened the
second play mode: the first **world transitions** landed 2026-08-03 (portals, a top-down overworld,
the Tunic camera; see §2a and the decisions block).
**Scale decision (settled 2026-08-05 after three rounds of walking it): the terrace step is
2.0 m.** 0.7 read as a kerb, 3.0 overshot into fortress walls; 2 — just over the 1.8 m player
— is where a terrace reads as a wall without dwarfing the map. `OverworldTileGrid.Step` is the
one constant; the generator, planner, ground and tests all derive from it, and the authored
map migrates by scaling levels (now Y 0/2/4/6/8; the migration commands live in the session
log — level = round(Y/oldStep), Y = level×newStep). Sea level is a fixed −0.35 m world
constant deliberately decoupled from the step. **East Mesa settled at level 1, so its 'Mesa
Trail' compiles** — the warning from the first build is resolved.

**Shadow settings tuned for the tile world (2026-08-05 morning):** what read as "z-fighting
between the stairs and the wall while moving" was shadow-map texel swim — a full coplanarity
audit of the built world (every triangle bucketed by plane, overlaps reported; worth keeping
as a technique) found ZERO same-plane same-facing overlaps. The single cascade refits to the
camera every frame, so on thin lips and fine risers the self-shadowing crawls. Fixes, both
persistent: `PC_RPAsset` main-light shadowmap 2048 → 4096, and the overworld builder's light
now uses custom bias (depth 0.4, normal 1.8, `usePipelineSettings` off — the editor asmdef
gained the URP runtime reference for it).

**The stair shimmer's second act (2026-08-05, after the recess landed):** Matt's theory was
coincident meshes — both audits said no. A duplicate-placement audit (zero pieces sharing a
position) and the coplanarity audit (zero same-plane same-facing overlaps) both came back clean
on the LIVE post-recess build, which redirected the hunt to shading: the tile material was
default URP/Lit (smoothness 0.5 — riser edges caught a moving specular gleam) and MSAA was OFF
(`msaaSampleCount` 1 — every nosing edge crawled). Fixes, all persistent: `EnsureMaterial`
forces matte on every regenerate (smoothness 0, specular highlights and environment reflections
keyworded OFF), `PC_RPAsset` MSAA 4, and the last bright hairline on the tread nosings turned
out to be the guide texture's WHITE GUTTER between UV islands bleeding under minification — the
guide renderer now DILATES island colours 8 px into the background before writing the PNG.
Rule of thumb: view-dependent stipple that survives the shadows-off toggle AND both audits is
shading (specular or texture sampling), not geometry.

**Third and final act (2026-08-05, later that morning — Matt: "it is structural to the models,"
and he was right):** the matte pass helped but the shimmer survived. Two diagnostics settled it,
both worth keeping. (1) A MOVING-PAIR DIFF — render the same view from two cameras 3 cm apart,
subtract, and every unstable pixel lights up; with the sun's shadows toggled off between pairs
the count barely moved, so shadows were finally exonerated for good. The diff showed the
UV-guide WIREFRAME blazing on every tile: a near-black one-texel line on light ground crawls
under minification everywhere at once. It is now an 18% darkening drawn with a blend, and the
guides import trilinear + aniso 8. (2) A NEAR-COPLANAR audit (0.005 m tolerance — the exact-
plane audit was too polite) found five junction families sharing planes: the cheek's end cap
sat IN the mouth wall's plane (the flickering patch at every stair foot), the cheek's front
shared the plane of the wall pieces' exposed cut ends beside the mouth, cheek rim tops were
flush with face rim tops, post rims ended on the face rims' back plane (WallT == RimOver by
coincidence), and the jamb post's front landed exactly on stair riser 4 (PostBulge 0.25 = 2 ×
tread depth). Fixes, all in the geometry: rims run three tiers (face 0.20 / cheek 0.22 / post
0.24), the cheek grew a proud tall end (0.02) and a proud face (0.005), posts bulge 0.27 with
flats at WallT+0.01 and rim backs at WallT+0.02, and face end skirts split off and inset —
a stacked piece's end skirt shared the lower piece's end plane. Frame-pair instability at the
stair notch: 118k hot pixels → 88k, remainder is silhouette aliasing. Matt confirmed the
jitter gone. THE RULE the tile set now obeys: overlapping pieces may interpenetrate freely,
but no two same-facing surfaces may share a plane — and constants that are multiples of each
other (WallT/RimOver, PostBulge/treadD) create shared planes by arithmetic accident.

**And runs are RECESSED (2026-08-05, morning, Matt): one tile at the bottom, one notched into
the wall** — the ALttP inset stair. `OverworldTileGrid.RampRecess = RampRun / 2`: the compiler
carves the run's top cells OUT of the high terrace (requiring high ground beyond for the
landing) and stands the rest on the low one. The cheek shrank back to a one-cell wall flanking
the notch, one rise tall; the band rule grows the jamb posts at the notch mouth on its own.
**Two hunts worth remembering from the morning:** (1) a CS0136 (`i` reused inside the ramp
loop) broke the compile SILENTLY — the console bridge showed nothing, a test run even reported
GREEN against stale assemblies, and play refused to enter; the tell was DLL timestamps older
than sources, and the real compile errors live in the PROJECT-LOCAL `Logs/Editor.log`, not the
AppData one (trap 23, now with the right path). (2) The planner's `EdgeMid` assumed +1 edge
directions; the cheek pass passes ±1 sides, so every west/south cheek landed one cell INSIDE
the wall — an invisible piece and a see-through hole at half the jambs. Canonicalized, and a
test now pins both cheek positions to the notch's exact edge planes.

**And climbs became RUNS (same night, Matt: one cell per 3 m was a ladder):**
`OverworldTileGrid.RampRun = 2` — a climb is a run of RampRun consecutive ramp cells, each
rising Step/RampRun, with a per-cell `RampIndexAt` (0 at the foot). The compiler converts the
boundary cell PLUS the flat cells behind it (a boundary without room for the whole run stays a
cliff and warns); connectivity is exact fixed-point in 1/RampRun level units — foot, run cells
and head chain on integer matches, side entry still refuses, parallel columns connect only at
the same run index. The stair piece is one run cell: 8 risers of 0.1875 (agentClimb-safe), so
a full climb is a 16-riser grand staircase over 2 m; the cheek spans the whole run as one wall.
**Run pieces carry a RunSkirt** — sides/underside reach (RampRun−1)·rise below their own foot,
because an upper run cell floats above the low terrace and its open flanks showed the sea
through every slot where a run met a wall. A long CanStep probe decomposes along its segment,
so foot-to-mid-run is a legal continuous walk, and there's a test saying exactly that. Suite
792 green.

**LAYERS (2026-08-05): one plane position can now carry two walkable surfaces** — a bridge
deck over a gully, an overhang over the road, a cave floor under a terrace. The flat-planar sim
survives untouched by design: `ITopDownGround` grew layer-aware default-interface overloads
(`CanStep(from, to, ref layer)` / `HeightAt(point, layer)`), the sim stores the OPAQUE token in
`CharacterState.GroundLayer` and threads it back without interpreting it (`Teleport` resets to
0), and presentation lifts by the layered height. **The token tracks the FEET, not the toes
(2026-08-05, Matt's teleport-at-the-edges bug):** `GroundPermits` probes with the leading edge
— centre and corners up to half a body AHEAD of the feet — and the first cut committed the
layer from those probes, so the token flipped one cell early and the body dropped through the
deck (or popped onto the cave roof) while the feet were still short of the boundary. The edge
probes now only VALIDATE the move; a separate feet-to-feet probe RESOLVES the committed layer.
`TheLayerTokenTracksTheFeetNotTheToes` pins it: the real factory sim walks
terrace → deck → terrace for 240 ticks and the layered height must never dip. `OverworldTileGrid` gained a sparse per-cell OVERLAY surface (one extra, flat
Ground — the deliberate cap of this model; overlay ramps are a decision for the feature that
needs them), authored as `OverworldMap.Layers` rect footprints: Y below the terrain = cave
floor (terrain becomes the roof), above = deck. Overlays over SEA are bridges over water — the
deck is the cell's only surface. **Connectivity resolves which surface a body is on** — a cave
mouth is just where the floor's edge height meets open ground; no mouth authoring exists. The
planner treats a connection that pierces a wall band as an OPENING (the mouth suppresses its
wall; a deck meeting a terrace at the band's top keeps the wall below), grows cave interior
walls from floor to rock top, and gives every overlay its own cap — whose sides and underside
now wear rock colour, because deck edges and lintels put them in the open. Demo authoring on
the live map: 'Foothills Hollow' (a cave in the south face) and 'Foothills Overlook' (a deck
over the heartland). Live-probed: into the mouth (layer 1, floor 0), onto the deck (layer 1,
height 2), under the same deck (layer 0, height 0). Suite 796 green.

**Layers reached the TRANSITION system (2026-08-05, Matt: handle teleporting to a side-scroll
scene from a cave, bridge, or overhang — and coming back):** three fixes, verified live as a
full round trip (Overlook deck, layer 1 → side-scroll course → back to the same spot on the
same surface). Portals tested feet at the flat rail depth — a deck portal could never fire and
a cave portal fired for a body on the roof; `PlayerCharacterHost.FeetWorldPosition` now carries
the layered ground height and portals (and the wanderer touch, which gained a same-floor height
gate) test against it. Arrivals reset the token — `ITopDownGround.LayerFor(point, height)`
picks the surface nearest a world height and `TeleportTo` seeds the token from the arrival's Y,
so spawn points authored inside caves or on decks stand on the right floor. And returns went to
fixed door spawns — `SceneDirector` now remembers every departure (feet WITH surface height,
facing) keyed by scene (keyed, not one slot: the return trip itself records a departure and a
single slot is overwritten before it is read — that bug lived for one test cycle), and a portal
targeting the **`@return` spawn id** arrives exactly where the player left. The traversal
course's exits use it, which is Zelda II's encounter rule: the fight interrupts the journey.

**THE OVERWORLD MAP TOOL (2026-08-05, Matt: "the real overworld will likely be done by hand —
can we create a tool?"):** the architecture already allowed it (one data asset, pure compiler,
pure planner, scenes storing recipes), so the tool is UI over existing seams plus one enabling
refactor. **`OverworldWorldBuilder`** extracts the host's assembly steps into a shared static
builder the play host and the editor preview both call — and buckets tile instances into
**16×16-cell chunk roots** (Matt: "will this be auto chunked?"): compile+plan re-run whole per
edit (pure C#, milliseconds), only touched chunks re-instantiate. Measured: 150 ms full 96×72
build in edit mode, 32 ms per painted patch. The host inherits the chunk structure — the hook
for streaming and per-chunk mesh combining later. **`Prophecy > Overworld Map Tool`** owns: a
hidden never-saved preview root (`OverworldMapPreview~`, orphan-swept, torn down entering
play); Scene-view handles for every shape noun (rect centre/edges/rotation disc, ramp
ends/width, course points with Ctrl+click insert / Shift+click delete); a **2D paint canvas**
(one texture pixel per cell, point-filtered — never a widget per cell; paints against the
COMPILED grid, stroke lives in a mirror dict, MouseUp commits one undo step via
`RegisterCompleteObjectUndo`); and **prop placement** (palette asset → armed ghost → analytic
ray-march onto the grid surface, R rotates, Esc disarms, Shift+click deletes, snap-to-cell).
The HYBRID GRAIN is data: `AuthoredCellOverride` (sparse; Terrain Ground/Sea + level, Road
Add/Remove) applies after regions+rivers and BEFORE ramps — painted land restores a carved
channel, painted terrace edges are rampable, painted road obeys the flat-or-deck invariant.
`AuthoredProp` (prefab, plane position, yaw, SurfaceLayer 0/1, BlockCells+BlockSize) spawns
under a Props root with height DERIVED from the grid at spawn (a terrain retune never strands
one); blocking stamps the compiled grid (`RasterProps`, last) and walkability treats blocked
as NO-SURFACE in both `HasSurface` and `EdgeHeight` — step-in refuses, the escape rule still
frees a stranded body, diagonals cannot thread a blocked corner. **Known gaps recorded:**
~~NavMesh never sees prop blocking~~ CLOSED 2026-08-05 — `CarveUnwalkables` in the shared
builder drops a carving `NavMeshObstacle` box on EVERY unwalkable cell (`UnwalkableAt` is
already the union of greeble masses, prop footprints and painted refusals), so the baked
mesh and the sim agree about refusal wherever anything steers by the mesh; block footprints
are axis-aligned, yaw is cosmetic; the rect rotation disc's sign and the canvas's north-up
orientation want one visual confirmation pass in use.

**BIOMES AND PROVINCES — DESIGN SETTLED WITH MATT, 2026-08-05 (build in progress):** the pause
after the tool's first drives produced the ontology conversation, and two nouns came out of it.
**BIOME is a SPLAT, not a bucket** (Matt's heatmap idea, upgraded from one scalar to per-biome
influence fields so any biome can border any biome): authored as feathered influence shapes +
a biome paint brush (the hybrid grain, one more channel), compiled to per-cell dominant +
secondary + blend. TWO consumers: (1) the GEOMETRY LUT — dominant biome + DeriveSeed(map seed,
cell index) → weighted variant pick from an `OverworldBiome` asset holding the SAME 17 slots
as the base tile set, empty slot → gray-box fallback (this is worldgen's picker stack lifted
whole — Biome/BiomePalette/BiomeVariantPicker were built for exactly this, just noise-fed);
(2) the SHADER LUT — raw blend weights baked to one world-space texture driving a single
triplanar terrain shader for base materials and cross-faded transitions (which is what hides
the discrete geometry flip at dominance boundaries); Consumed darkening reserved as a future
channel of the same texture. Variation law: variants may change GEOMETRY as long as the mating
envelope holds (cut-end planes, rim tiers 0.20/0.22/0.24, skirt reaches, flat walkable tops —
the contract constants are cross-biome law); the workflow is gray-box everything → paint
influence → majority auto-fills → hand-pin required tiles and important props. Edge pieces:
the HIGH cell owns its walls, land owns its shore. **PROVINCE is the gameplay region — 
EXCLUSIVE, never blended** (biome is how it looks; province is whose rules apply): an
`OverworldProvince` ScriptableObject (name, spawn table, cadence, encounter section — tunable
in play mode like all tuning). **CORRECTED BY MATT, then BUILT: provinces have NO bounds of
their own — the NAMED SHAPES are the provinces.** Terrain regions, biome areas and thickets
carry an optional `Province` reference; the compiler stamps cells in raster order (later
wins), and 'the Westwood' is a province because the grove is, not because a rectangle was
drawn twice. AUTHORED FROM THE TOOL (Matt's requirement): Select mode grew a selection panel —
rename any shape, assign or NEW-button-create its Province — biome areas and thickets gained
scene-view rect handles (they had none), and the minimap gained a Province View toggle +
hover names. Also added on Matt's call: a plain UNWALKABLE paint channel — Block + paints
bare blocked cells (no scatter: rocks, rubble, not-this-way), and **Block − is the hand's
LAST WORD, unblocking anything including thickets and prop footprints** (applied dead last in
the compile). Demo: Province_Heartland (EMPTY table = safe — the authored end of wanderers
eating playtests) on the Heartland region, Province_Westwood on the grove; probed live.
**The spawner/contact WIRING is BUILT (2026-08-05 night) and the wanderers are back ON.**
`OverworldEncounterSpawner` MOVED into the Overworld assembly (git mv, GUID kept — provinces
are a grid question; the sim seam was never meant to keep the overworld's own presentation
off the grid) and owns NO prefab/cadence/cap anymore: the player's cell's province supplies
all three each beat, wilderness rechecks every 2 s and never spawns. Spawn placement asks
`OverworldEncounterRules.CanHostAWanderer` (plain Ground, not unwalkable, in bounds — no
ramps, water, bridges): 8 bearings tried, all refused skips the beat, no clamping. Contact
resolves from the ground under the PLAYER's feet via `OverworldEncounterRules.ResolveContact`
(road cell + roadScene → safe crossing; else contact cell's province.EncounterScene; else
fallback; EMPTY resolved scene = the touch is shrugged off). All three rules are pure statics
in `OverworldEncounterRules.cs`, pinned by tests. Builder wires _gridHost + road/fallback
strings (all at GrayBox_Traversal/centre until real battle scenes exist) and enables the
spawner — safety is authored now. Demo: Province_MesaMoor (Enemy_Wanderer ×1, 8 s cadence,
MaxAlive 3) on the Mesa Moor biome area. LIVE-VERIFIED: 3 spawned on the moor and capped;
wanderers at the mesa FOOT (y=0) could not touch the player on the terrace (y=2) — the
touch height gate doing its job; same-floor touch carried the player to GrayBox_Traversal
centre. ~~KNOWN GAP: wanderer ROAMING ignores the grid~~ CLOSED 2026-08-05, same night —
the wanderers are TIED TO THE NAVMESH (Matt) via the agreed oracle shape, see the routing
paragraph in the decisions section. Live probe: wanderers at y=0.0 → y=0.5 → y=2.0, walking
the STAIRS onto the mesa and catching the player on top — where before the oracle they
piled at the cliff foot forever.
**Encounters resolve at CONTACT TIME by LOCATION (Matt): the systems are separate.** The
player's province drives WHAT spawns around them (empty table = safe province — the authored
end of wanderers eating verification passes); wanderers roam FREE, no leash; on contact the
CONTACT CELL is polled — road flag → the safe side-scroll section (Zelda II's road rule:
wanderers can catch you on a road, you just get the safe crossing), otherwise the cell's
province → its encounter section. The wanderer carries WHO you fight; the location carries
WHERE. Build order: biome splat S1 (influence data/compiler/brush) → S2 (biome asset +
picker) → S3 (triplanar + LUT) → S4 (seeded scatter) → provinces. **S1 and S2 are BUILT
(2026-08-05 evening): `AuthoredBiomeArea` feathered influence + the Biome brush compile to
per-cell dominant/secondary/lean; `OverworldBiome` (17 slots, weighted variants, its own file —
a ScriptableObject asset needs its class-named file or the script ref breaks) +
`OverworldBiomePalette`; `TilePlacement.OwnerCell` threads the ownership rules through every
planner emit; `OverworldBiomePicker` rolls variants from DeriveSeed(map.Seed, cell×31+piece).
Proven live: the 'Test Moor' biome (one recoloured cap variant) + the 'Mesa Moor' demo area —
the mesa wears moor-brown caps, everything else falls back gray-box. One rendering trap paid:
a variant material without `_ENVIRONMENTREFLECTIONS_OFF` reads NAVY from overhead (sky
reflection); matte-flag every gray-box material fully.** **S3 BUILT same evening:** the LUT
(one texel per cell, R/G indices + B lean, A reserved for Consumed) bakes pure
(`OverworldGroundLut.Bake`), and `Prophecy/OverworldGround` — a hand-written URP shader with
manual four-tap bilinear (indices cannot filter, colours can), triplanar detail hook, inline
ShadowCaster/DepthOnly — drives ONE shared cap material; rebuilds refresh the texture in
place. The Test Moor's ground moved to the shader (GroundColor) and the mesa cross-fades
moor-brown into heartland green over the feather. Missing palette entries render magenta on
purpose. **S4 BUILT (Matt's scatter rule: scatter fills NON-WALKABLE masses only — open space
is for travelling; dress it with hand props or shaders):** `AuthoredThicket` rects + Trees +/−
brushes plant THICKET cells — blocked exactly like prop footprints (ground-only, never a road
cell: the road is the carved path through the forest, and painted Remove carves clearings) —
and `SpawnScatter` fills each with one seeded weighted pick from the biome's `Scatter` list
(fallback: the palette's `DefaultScatter`, wired to Prop_Tree), jittered position and yaw,
deterministic per cell forever. Demo: the 'Westwood' grove — 80 trees, one solid impassable
canopy. Also fixed en route: the canvas mirror was not copying the Biome field, so any paint
commit silently erased painted biomes.

**ELEVATED WATER AND WATERFALLS (2026-08-05, Matt: "focus on the elevated water and
waterfall"):** water gained a LEVEL with zero new authoring. A sea cell always stored a level;
it was just always 0. Now a river INHERITS the level of the terrain along its course,
monotonically non-increasing from source to mouth (sampled BEFORE carving — a carve that runs
ahead of the sampling raises the downstream terrain to its own water level and the fall never
drops; that bug lasted one test cycle). So water holds its terrace's height and falls exactly
where the land falls. Everything downstream of that one idea generalized rather than grew:
`FaceEdgeLevel` lets water present its own level (cliffs beside a gorge stop AT the water),
the shore branch works RELATIVE to the water's level (an elevated lake bank is the same
relationship as the coast — same pieces, higher Y), corner posts read water levels into the
band rule, and `PlanWater` adds a per-cell quad at each elevated cell's own waterline over the
one ocean plane. WATERFALL SHEETS hang where water at level N meets water below: pure
`TilePiecePlanner.PlanWaterfalls(grid)` (testable), meshed by the host like roads (they are
sheets, not tile pieces — placements carry no pitch), wearing the water tile's material with
all UVs pinned to the island's centre (a mesh with no UVs sampled the guide's background and
rendered WHITE — the one bug the capture caught). Elevated water beside LOWER LAND has
nowhere to go and warns like an inert ramp; beside lower water it is a fall. Out of bounds is
sea level 0, so a source carved into the map's border cell pours off the edge of the world —
real behaviour, and why the test authors its source inside the terrace. Demo: the Eastwater
now SOURCES on the North Rise and falls off its south cliff through a gorge gate framed by
jamb posts. Suite 801 green.

**RIVERS AND ROADS (2026-08-05, same session):** the map grew three authoring nouns.
`AuthoredRiver` — a polyline course carved to Sea cells AFTER regions, BEFORE ramps (a channel
cuts terraces into gorges, but never converts a climb into a river bed); banks, water and
refusal all come free because a river cell IS a sea cell — the coast machinery does not know
the difference. Crossing one is an `AuthoredLayer` deck at bank height: a bridge over water was
already the overlay model's native case. `AuthoredRoad` — PAINT, not ground: a polyline flags
flat Ground cells (and any cell with an overlay — that is the road riding its bridge deck); the
host draws one merged strip mesh 0.02 above the walk surface under scenery, so the NavMesh and
the sim never see it. Sloped run cells carry no road — an ALttP road breaks at a stair. Demo
authoring on the live map: the 'Eastwater' runs from the north sea past the North Rise (clipping
its corner into a two-terrace gorge) to the south sea, and the 'Mesa Road' runs from the
Foothills ramp foot past the spawn, across 'Eastwater Bridge', to the Mesa Trail. Suite 800
green (river carve + bridge crossing, road paint + deck riding, LayerFor arrivals, seeded
teleports). **Known gap:** the
overhead camera cannot see INTO an enclosed cave (the roof occludes) — a roof cutaway when the
player is beneath is the natural next slice; overhang mouths and bridges read fully.

**Resume at (2026-08-04, evening): the 3D TILE STRUCTURE is BUILT AND LIVE.** The overworld now
compiles `OverworldMap` into a discrete `level[x,z]` grid (`OverworldTileGridCompiler`), plans
tile pieces from it (`TilePiecePlanner`, pure C#), renders the 17-piece generated tile set, and
answers the ground seam from the same cells (`TileGridGround` — the seam's third and best
implementation, now the default authority). 792 tests green, verified in play. The Stålberg
continuous-elevation approach that this replaces fought back (see "the reachability saga");
connectivity is now authored data rather than geometric inference. **One authoring item is
loudly flagged at load: East Mesa's Y=1.1 quantizes to level 2 and its 'Mesa Trail' ramp
compiles to nothing (a ramp cell joins exactly one step) — re-author the mesa to Y=0.7 or give
it two terraces.** Everything else — transitions, portals, encounter loop, camera — is finished
and stays.

### The 3D tile structure — agreed direction, next session's build

- **Data:** a plain grid, `level[x,z]` (integer elevation, one step = one authored unit) plus a
  per-cell kind: Ground, or Ramp with a facing (a ramp cell joins level N to N+1 along its axis).
  Cliff faces are not data — they are the automatic consequence of two neighbours differing.
- **Compile the existing `OverworldMap` into it** — keep the authoring surface (regions quantize
  to levels, authored ramps become runs of ramp cells). The asset survives; only the compiler
  behind it changes.
- **Walkability comes straight from the data**: neighbours connect iff same level, or a ramp
  cell joins them. Exact, plain C#, headless — `ITopDownGround` gets its third and best
  implementation, and the NavMesh (still baked for AI routing) can never disagree with it
  because both derive from the same cells.
- **Rendering:** flat tiles at `level × step`; cliff-face pieces where neighbours differ; a
  distinct ramp/stair tile for ramp cells — the ALttP grammar as art, finally legible. The
  existing Stålberg tile prefabs can serve as placeholders for ground/edges.
- **The worldgen package is NOT discarded**: it remains for the side-scroll encounter topology
  pipeline (`ILevelConstructor` seam), and its organic mode stays available for biome interiors
  if ever wanted. Only the overworld's GROUND changes system.
- **The tile set is specified AND BUILT — 2026-08-04:** 17 pieces in a three-family
  decomposition (cell / edge / corner-post); `Plans/Overworld-Tile-Set.md` is the placement
  contract the renderer will be built against. The Meshy generation path was tried and retired
  same-day (output didn't hold the contract); the meshes are **generated procedurally** by
  `Prophecy > Build > Generate Overworld Tiles` into `Assets/_Prophecy/Art/Tiles/` — mesh +
  prefab + material + colour-coded UV guide PNG per tile, layouts documented in the generated
  `Plans/Tile-UV-Guide.md`. Reskin = paint textures over the guides (a real base map survives
  regeneration), or a triplanar shader later — Matt is considering one; the hard-edged
  axis-aligned geometry suits it.
- **The renderer and compiler landed the same evening — the overworld runs on the tile grid.**
  `Runtime/Overworld/`: `OverworldTileGrid` + `OverworldTileGridCompiler` (regions quantize to
  0.7 m levels, later regions win overlaps; authored ramps convert the LOW cell at each one-step
  boundary in their strip to a Ramp cell facing uphill — a ramp that finds no boundary warns and
  compiles to nothing); `TileGridGround` (ITopDownGround from the cells — same level connects,
  ramps join N→N+1 end-on and refuse sides, parallel ramp columns connect, sea refuses, escape
  rule kept; **the default GroundAuthority**); `TilePiecePlanner` (pure: caps, ramp/stairs by
  a host toggle, greedy-3-split faces, band-rule corner posts, cheek pairs, shore, one water
  plane); `OverworldTileSet` (auto-filled). `OverworldGridHost` was rewritten around them —
  worldgen is no longer referenced by the overworld path. **NavMesh bakes from the walkable tops
  only** (caps/ramps/stairs under `Ground_Walkable`; cliffs are absent from the bake, not merely
  steep), slope 40 (the 35° ramp must walk), climb 0.45. Tests: `OverworldTileGridTests` ×16.
  **Same-evening fixes after first look (Matt spotted both):** the cheek's mirror twin is a
  BAKED piece (`CLF_CheekM`) — negative scale reverses winding and renders inside-out; the shore
  cape is a 45° chamfer wedge whose sides mate with the shore edges' end planes; the shore
  INNER post was cut entirely (two banks meeting inward already intersect in the correct valley
  — any patch is coplanar with both and shimmers); post rims ride 0.02 proud of face rims for
  the same coplanarity reason. Still 17 pieces. **Two more artifacts hunted the same evening:**
  the "dark stripe across the sea" was the water tile's own UV-guide texture — every guide has
  its triangle wireframe drawn on, and the water is the one piece that stretches (×104), so its
  triangulation diagonal became a sixty-metre line; the guide renderer now skips wireframe for
  `WTR_Fill`. The faint line that remained after that was the **shadow cascade seam** crossing
  the flat sea — `PC_RPAsset` now runs **1 cascade** (was 4): a fixed overhead camera at ~36 m
  over a 96×72 map gains nothing from cascade splits, and the seam was the only thing they
  drew. Diagnostic that solved all of these: toggle the sun's shadows off — what survives is
  geometry or texture, what dies is shadow.

### Why (the reachability saga, 2026-08-04 afternoon — read before re-attempting Stålberg elevation)

Matt's ask "use the NavMesh to test reachability" produced an audit (CalculatePath from spawn to
every terrace) that drove out four real defects and one conclusion:

1. **The painter's region sort was UNSTABLE on equal keys** (`List.Sort` by origin only) — which
   overlapping same-origin region won a vertex was an introsort artifact; two terraces lost
   their elevation entirely to flat regions beneath them. FIXED in the package: tie-break by
   region id (registration order). Golden seeds unaffected, 776 green. Migration entry 8.
2. **AxisAligned paint shape in Rectangular mode ate coastline** — 1,351 perimeter wall-ring
   vertices; the host now paints AxisAligned only on Organic maps. FIXED.
3. **Ramps flush at plateau edges pinch off** under the agent radius — extended past both ends.
   PARTIALLY effective.
4. **A mid-plateau ramp paints a trench**, and its walls pinch against neighbouring cliffs;
   grade-vs-plateau lips of 0.2–0.6 m are NOT merged by agentClimb (0.45 tried). Re-siting
   individual ramps fixed some and re-broke others — final audit **4/7 reachable** (Foothills,
   North Rise, North Shelf, East Mesa OK; Bluffs, Highlands, Crown cut off).

The conclusion: with continuous painted elevation, connectivity is an emergent property of mesh
geometry, and every authoring edit re-rolls it. Discrete levels + ramp-kind cells make
connectivity a stated fact. That is the 3D tile structure.

**Current live state:** Rectangular lattice at 1 m tiles (14,403 placed), all seven elevations
painting correct heights, 4/7 reachable, wanderers off, NavMesh authority with slope 25° /
climb 0.45. The reachability audit exists only as RunCommand snippets in the session log —
worth enshrining as `Prophecy > Overworld > Audit Reachability` when the tile build lands.

The previous thread text follows for context: the overworld on the Stålberg grid. HopeFell's
`com.rokkan.worldgen` (the Townscaper-style irregular grid + four-stage generation pipeline) was
adopted into the shared packages 2026-08-04 — 768 tests green in this project, golden-seed
determinism intact across the 6000.3→6000.5 jump; adoption notes in `MIGRATION-HopeFell.md`.
Next: hand-author overworld regions onto a Stålberg grid and let the tile placer populate it
(the `Biome/Settlement/Road/River/Terrain/Landmark` region kinds are reserved and waiting), then
a lane-space `ILevelConstructor` for side-scroll random encounters off the topology stage. After
that: top-down collision (gap 3 — the grid's `isFloor` queries are the natural answer), real
entrances, "last overworld position". The **combat orchestrator** (§11.11) remains the queued
combat thread, then finishers, stat hooks, DOTs and the death rule.

Enemies now exist and plan: four archetypes on one GOAP brain format, driving the simulation
through the same `InputFrame` a gamepad produces. The roster is stable and verified — patrol,
pursue, swing, spring, retreat and fire all run, hits land, and the whole thing is guarded by
structural tests rather than by memory. What it is *not* yet is tuned: see §6, and note that the
orchestrator is the next thing standing between "it works" and "it plays".

This is the "where we are and why" document. Standing rules live in `CLAUDE.md` at the repo root
(loaded automatically) — this file does not repeat them. Design canon is `Plans/Design-Bible.md`;
the phase plan is `Plans/Gray-Box-Build-Plan.md`; the animation rules are
`Plans/Animation-Contract.md`; dev-only settings that must be reverted before shipping are in
`Plans/Release-Checklist.md`.

---

## 1. Current state

| | |
|---|---|
| Branch | `baseline/unity-project-and-design-docs` (**still not merged to `main`**) |
| Tests | **539 passing, 0 failed, 0 skipped** (refresh from `Logs/test-results.txt`) |
| Unity | 6000.5.0f1, URP active, Input System only, Cinemachine 3.1.7 |
| Uncommitted | An **Iron Roc Warrior** Meshy import (`Assets/MeshyImports/…162720/…162724/`) and the `GrayBox_Arena` edit that placed it in the scene — user experiments from 2026-08-03 16:27, deliberately left out of the transition commits. |

To put the work on `main`: `git checkout main && git merge --ff-only baseline/unity-project-and-design-docs`

### Commits, newest last

```
339a7f3  Add the combat timing spine: TickRange, ScalableWindow, AttackTimeline
c435894  Resolve hits and run combos: HitResolver, Hurtbox, ComboRunner
738818e  Wire attacks into CharacterSim: AttackModule, CombatTuning, the target seam
1aa5586  Add the combat arena: a demo you can swing in, and the overlay to read it
7c661a5  Answer the attack: block, parry, i-frames and hit-react, all in ticks
70d362e  Give the down-thrust its blade, and let it bounce itself
40ddd8f  Build the dodge step, the one thing that is intangible on purpose
a92437a  Respawn on death, so combat can be lost and then tried again
3e01cf7  Hold the dive, and give combat things that are not swords
5b8355c  One button for guard and parry; contact damage; a visible wind-up
3df8c7e  Record the netcode decision: co-op PvE, host authority, HopeFell first
7260df9  Own the hit geometry, add a broadphase, move the fight out of presentation
b80980e  Bound the scans the broadphase pass missed
44c7561  Refresh the handoff, and make its test numbers self-maintaining
fb5d7dd  Fix five defects a review found, and cover them
f55c3d2  Set up the animation system, and list what it needs
39a8a46  Pull a curated Synty clip set, and record what it took
f13a896  Record that the Hero rig is no longer a blocker
15bf0a0  Record the animation contract, match clip speed to the sim, put the hero on the player
a006490  Turn the hero to face the camera's plane, and give it its texture
2f8391c  Frame the camera by the character's share of the screen, not by a lane count
ed48b28  Measure the vertical dead zone in jumps, not lanes
84e0017  Hold the landing pose, and speed the camera up to match the tighter frame
ac58c8c  Show the body-state history on the F2 overlay
5e8a736  Log body-state transitions to a file so a flicker can be caught after the fact
97d0b85  Fix the flicker detector, and log what the blend actually did
3a13ae3  Show the run when landing out of a run, not the landing
b45bd21  Add the three-stat system, and wire Might and Heart into combat
ba1d5eb  Size the stat arrays from the enum instead of from the number three
837ecf1  Close the stat gaps against HopeFell, and record the three refusals
e8c8d26  Record the stat parity audit in the handoff
c55d2c0  Give debuffs stacking rules: refresh, strongest, then cap at one
ffa95d5  Add speed modifiers and ability restrictions, and stop the playback clamp drifting
```

### Three repos are in play

| Repo | Path | State |
|---|---|---|
| **Prophecy** | `RGS\Prophecy\Prophecy` (Unity project nested one deeper) | active, above |
| **Shared packages** | `RGS\Packages` | **changed** — `com.rokkan.animation` extracted |
| **HopeFell** | `RGS\HopeFell` | **still untouched, and must stay that way** |

`com.rokkan.animation` **0.1.0** came out of HopeFell's `com.rokkan.gameplay` this session and is
consumed by Prophecy. Divergences are logged in `RGS/Packages/MIGRATION-HopeFell.md`, entries 7-9.
The two that matter: root motion is gone from the contract, and the vestigial gameplay clip-event
channels were deleted rather than annotated.

HopeFell's **stats** system was read in full and deliberately *not* extracted. The reasoning, plus
three latent bugs found while reading it, are in the same file. Nothing in HopeFell was modified.

---

## 2. What exists

### Combat — `Runtime/Sim/Combat/`

```
TickRange.cs          half-open [Start, End); zero-length is ABSENT, not instantaneous
ScalableWindow.cs     start + duration; the opening tick never moves, only the length scales
AttackHitBox.cs       one damaging volume: window, offset, half-extents, rotation, damage, cover
AttackDefinition.cs   phases as three DURATIONS that partition the action; windows laid over them
AttackTimeline.cs     arms once, resolves windows at Arm, reports what is true this tick
HitResolver.cs        separating-axis overlap + CollisionWorld.IsOccluded for cover
Hurtbox.cs            Hurtbox + Attacker; the Attacker carries the damage scale it swung with
ComboRunner.cs        chain links gated on the cancel window, with input buffering
ICombatWorld.cs       the target seam: hurtboxes in, HitEvents out, HitResults back
CombatTuningData.cs   the moveset, stance entry points, buffer length, defence numbers, Validate()
Damage.cs             HitOutcome, HitResult, IDamageGate + GateOrder, PendingStun
Vitals.cs             health, owned by CharacterSim so a test can kill something
HitSweep.cs           swing one volume, dedup per box/target/action, notice being parried
HurtboxSet.cs         this tick's volumes, bucketed on X — the broadphase
CombatState.cs        the fight itself: registry, projectiles, contact, hit routing. Plain C#
Projectile.cs         ProjectileDefinition + ProjectileSystem: one type for bolts and areas
```

### Stats — `Runtime/Sim/Stats/`  *(new this session)*

```
StatKind.cs           Might/Flame/Heart are earnable; Speed and anything below is modifier-only
StatModifier.cs       a struct: stage, value, expiry TICK, source, id, stack key
StatModifierSpec.cs   the authored half — a DURATION, resolved to an expiry when applied
StatBlock.cs          levels + modifiers + derived numbers. Implements IStatSource
StatTuningData.cs     what a level is worth, and the Resolve curve. Derived, never stored
StatScale.cs          "this scales off that stat" — for skills and attacks
IStatSource.cs        the seam, plus FixedStats for enemies and tests
AbilityRestriction.cs silence / disarm / root, and the set that folds them
Runtime/Core/StatTuning.cs   asset shells: StatTuning and StatProfile
```

### Animation — `com.rokkan.animation` *(shared)* + `Runtime/Presentation/`

```
com.rokkan.animation/AnimationSystem.cs   PlayableGraph; 4-slot state layer + an injection layer
com.rokkan.animation/IClipInjector.cs     the surface; NO root-motion flag, deliberately
com.rokkan.animation/ClipHandle.cs        per-play subscription scope; OnDone fires exactly once
com.rokkan.animation/ClipEventChannel.cs  cosmetic channels ONLY — no gameplay vocabulary exists
BodyState.cs                              23 states; the authoritative animation shopping list
BodyStateResolver.cs                      pure: sim state in, one BodyState out. Precedence tested
BodyAnimationSet.cs                       state -> clip + authored reference speed
CharacterAnimator.cs                      reads the sim, speed-matches, logs flickers (editor only)
Editor/Build/BodyAnimationSetBuilder.cs   generates the mapping from measured speeds
Editor/Build/HeroModelInstaller.cs        puts the rigged hero on the player prefab, scaled
Editor/Build/ReferenceSpeedMeasurer.cs    measures what each clip was authored to travel at
```

### The rest of combat

```
Runtime/Sim/Abilities/AttackModule.cs   the join: lock, timeline, resolve, publish cancel window
Runtime/Sim/Abilities/Block.cs          guard AND parry on one button — when you pressed decides
Runtime/Sim/Abilities/DodgeStep.cs      a committed step with i-frames; distance authored
Runtime/Sim/Abilities/HitReact.cs       picks up a parked stun, force-locks, shoves; i-frame gate
Runtime/Presentation/CombatDirector.cs  thin: owns a CombatState, ticks it, drives the visual half
Runtime/Presentation/Combatant.cs       hittable: a dummy, or a front for a simulated character
Runtime/Presentation/TrainingAttacker.cs a dummy that swings back, or casts, on a tick cycle
Runtime/Presentation/CombatDebugOverlay F2: frame data, defence state, body state, GL volumes
Assets/_Prophecy/Data/CombatTuning.asset        the live moveset and defence numbers
Assets/_Prophecy/Data/BodyAnimationSet.asset    generated; 17 states mapped, 6 unmapped
Assets/_Prophecy/Animation/Synty/               25 clips + 2 rigs, 11 MB, licence beside them
Assets/_Prophecy/Scenes/GrayBox_Arena.unity     generated; 12 stations, 10 warp points
```

There is **no `Parry.cs`** — parry folded into `Block` when the two became one button.

### Tests, per fixture

```
combat      DefenceTests 39, AttackModuleTests 26, DodgeTests 23, CombatScaleTests 21,
            DownThrustCombatTests 21, AttackTimelineTests 18, ProjectileTests 17,
            HitResolverTests 16, ComboRunnerTests 14, CombatWindowTests 14        = 209
stats       StatTests 41                                                          =  41
animation   BodyStateTests 22, StateBlendTests 9                                  =  31
movement    MovementTests 33, TraversalAbilityTests 32, CharacterSimTests 23,
            CollisionWorldTests 22, OcclusionTests 11, InputLatchTests 7          = 128
contract    SimArchitectureGateTests 6, PackageWiringTests 4                      =  10
shared      SimClockTests 14, RGS_RandomTests 9, RandomStreamTests 7, and four of 6
            (DeterministicMath, FoundationType, GoldenVector, RandomSource)       =  54
```

**419 Prophecy + 54 shared = 473.** The shared ones run because `com.rgs.core` and
`com.rokkan.animation` are in `testables` — if either count drops to zero, that is the manifest
entry, not a deleted test.

### Built, but not yet connected to anything

Half the stat surface is waiting on enemies, and that is the right order: each is a one-liner once
there is something to hang it on, and guessing now would be guessing.

| Built | Waiting on |
|---|---|
| `Flame` -> `MaxFlame` | `FlameArt` is still a stub |
| `Resolve` / `SpendLevel` | **nothing awards XP** — no deaths grant it, no UI spends it |
| `StatProfile` | `Combatant` has no stats field |
| `StatScale` | no `AttackDefinition` scales off a stat |
| `AttackModifiers` window scales | gear, which does not exist |

**Six animation states have no clip** and silently fall back to Idle: `WallSlide`, `LedgeHang`,
`LedgeClimb`, `LadderIdle`, `LadderClimb`, `DownThrust`. Synty ships nothing for any of them — they
are 3D action packs and these are 2.5D platformer moves, so a wall-slide currently looks like
standing still. Jump variants are also unmapped, so a sprinting jump plays a standing leap.

**Not built at all:** no encounter concept, no DOTs. **Death is a respawn and nothing more** —
deliberately scaffolding, see §7. `Crawl` and `FlameArt` are the last two ability stubs. *(This
list used to open with "no enemies, no AI, no overworld scene" — all three exist now; see §8 for
the roster and §2a for the overworld.)*

---

## 2a. World transitions — first slice, 2026-08-03

The dual-mode structure is real: walk off either end of the traversal course and you are standing
in a top-down overworld under a fixed overhead camera; touch the blue cube and you are back,
mid-course.
Verified end to end in play mode — both portals, both arrival spawns, the return trip twice, the
space switching `SideScroll` ⇄ `TopDown` each way.

```
Runtime/World/Portal.cs                     walk-in volume; fires GoTo(scene, spawn); arming rule
Runtime/Presentation/OverworldCameraRig.cs  the overhead camera: fixed angle, share-derived distance
Editor/Build/GrayBoxOverworldBuilder.cs     generates GrayBox_Overworld; menu + -executeMethod
Editor/Build/GrayBoxMaterials.cs            the one portal material both scenes share
Assets/_Prophecy/Scenes/GrayBox_Overworld.unity  TopDown, kill plane off, own camera at priority 50
Tests/Editor/PortalTests.cs                 the arming rule, 5 ways
```

- **The traversal course now spawns mid-level** at `SpawnPoint` id `centre`, placed by snapping
  the course's arithmetic midpoint into the nearest recorded ground-level floor span — so "the
  middle" stays standable through any retune. Portals bracket the course: `Portal_West` on the
  run-up edge, `Portal_East` in front of the end wall.
- **The overworld is deliberately almost nothing** — a 64×44 m plain, a rim you can see (visual
  only), two arrival spawns flanking the return cube. It exists to prove the mode loop, not to be
  a place yet.
- **`SceneDirector.Player` is new** — portals read the player's mapped feet from it rather than
  going looking.
- **Bootstrap's brain blend is now Cut** (was EaseInOut 2 s). See the decisions block.

### Second slice, same day — feel feedback answered

Three user complaints ("the teleport feels bad", "movement is slow and awkward", "make it Zelda 2
— things pop up and wander around the player"), three answers, all verified in play:

```
Runtime/Presentation/TransitionVeil.cs      black cut covering the whole transition (IMGUI, no deps)
Runtime/Sim/AI/RoamSteering.cs              the wanderer's walk as a pure hash-seeded function
Runtime/Goap/RoamStrategy.cs                the first two-axis GOAP action; Running forever like the chaser
Runtime/World/OverworldEncounterSpawner.cs  pops wanderers up around the player, retires the distant
```

- **The veil.** `SceneDirector` shows it for the entire transition — including the adopt path —
  snaps the *arriving* scene's `OverworldCameraRig` after placing the player (it initialised
  against the pre-teleport position), and holds one frame past the snap so the first visible
  frame is the final shot. Every early-out in `Transition`/`Enter` hides it, because a stuck
  curtain is the worst regression this can have.
- **Top-down tuning.** Speed 5 → 7.5 (matches `RunSpeed`), acceleration 50 → 75, friction
  60 → 90 — asset AND code defaults, they drift apart otherwise. Overworld camera damping
  0.7 → 0.45, dead zone tightened.
- **The Wanderer** is the fifth archetype and the simplest brain in the roster: one `Roam`
  action, one goal. `EnemyIntent` grew `MoveY` (crouch beats it — ducking is a decision,
  drifting south is not); `RoamSteering.Heading(who, when, …)` is stateless because the strategy
  is a shared asset and per-agent dice on it is the isolation trap again. Bias 0.55 toward a
  seen target, re-rolled every 90 ticks, at 75 % speed so the player can outrun the menace.
  **Its sight is 20 m where everyone else's is 12** — the spawner places it 14 m out, and a
  wanderer born beyond its own sight drifts at random and is retired without ever menacing
  anyone. That bug happened; the wiring in `EnemyBuilder` is the fix.
- **Touching a wanderer IS the encounter — 2026-08-04.** The chip-damage placeholder is gone
  (the wanderer deals no contact damage, like Zelda II's map blobs): a wanderer within 0.9 m of
  the player's feet springs `SceneDirector.GoTo` to the side-scroll course, resolved by the
  spawner the way a portal resolves — presentation-side, a plane-distance test, behind the veil.
  Damage punishes a touch; an encounter *answers* it. No arming rule needed, unlike portals: the
  transition unloads the overworld and its wanderers with it, so arriving inside one is
  geometrically impossible. The target scene/spawn are two strings on the spawner
  (`GrayBox_Traversal` / `centre`) — when a real battle scene exists, that is the one thing to
  re-point. Verified in play: touch → traversal centre, health untouched at 100.
- **Top-down truths learned:** the overworld bakes NO colliders, ground included — the XZ
  projection turns a floor into one giant solid that occludes every line of sight; ledge/wall
  probes (`ShouldTurnBack`) are side-scroll questions and are now skipped in top-down; the
  zero-solids warning only fires in side-scroll, where it is actually a bug.

### Third pass, 2026-08-04 — the transition became a scene, and hurtboxes learned their shape

- **The veil is an animation, and the world freezes under it.** Fade out 0.4 s over the still
  world, hold black ≥ 0.25 s while the swap happens (a slow load stretches the black, never the
  fade), fade in 0.4 s, and only then does `IsTransitioning` drop. The simulation is **paused for
  the entire sequence** via `SimClockDriver.Paused` (new shared API — migration entry 7): the
  thing that touched you holds its pose, everything freezes together, and resuming costs nothing
  because the pause withholds time rather than skipping ticks. The veil itself runs on unscaled
  time — the curtain is immune to the freeze it accompanies. The veil owns its pacing;
  `SceneDirector` obeys (`IsOpaque` → swap → `HoldSatisfied` → `BeginUncover` → `IsClear`).
  **Trap fixed while building it:** error paths must not touch the veil — `Reveal()` owns the
  fade-up, and an early `BeginUncover` deadlocks its wait for the hold.
### 2026-08-04 — the overworld stands on the Stålberg grid

The flat plain is gone. `GrayBox_Overworld`'s ground is now an organic island built at scene load
by **`OverworldGridHost`** (new assembly `Rokkan.Prophecy.Overworld` — separate for the same
reason GOAP is: the worldgen package drags Burst/Collections, and the main runtime must not).
The pipeline is the package's inspector-only path: `CreateWorldGrid` (HexOrganic) → authored
regions as `Region.Room` footprints → `VertexTopologyPainter.Paint` → `StalbergTilePlacer`.
Verified live: 3,453 vertices / 3,381 quads built on entry, twice, zero errors.

- **`OverworldMap.asset` is the hand-authoring surface** (`Assets/_Prophecy/Data/`): seed,
  spacing, jitter, bounds, and a list of named rect footprints whose union becomes the land.
  The builder creates it **only if missing** — a regenerate must never flatten someone's
  authoring. Starter layout: Heartland + three rotated limbs, sized so the existing furniture
  stays on dry land.
- **Placed tiles are collider-stripped by the host** — the flat plain's occlusion lesson,
  enforced where the tiles are born. Registry disposed in `OnDestroy` (NativeContainers).
- Next here: per-region tile sets (the darkening), grid-backed top-down walkability
  (`isFloor` instead of colliders), and the reserved region kinds (Settlement/Road/River…).

- **Hurtboxes are shaped for their space.** `Hurtbox.ForBody` is the standing silhouette —
  plane-Y extent is the body's HEIGHT, centre raised half a height — which in top-down dragged a
  1.8 m box behind every 0.7 m body, half a height north of where it stood. That read as "the
  hit boxes are too large"; it was the wrong silhouette entirely. `Hurtbox.ForFootprint` is the
  view from above (width both ways, centred on the feet) and `Combatant.BuildHurtbox` picks by
  `state.Space`. `TopDownHurtboxTests` pins both shapes.

---

## 3. Decisions already made — do not re-litigate

Carried forward and still true: **feel first**; **sim owns its own `CollisionWorld`**; **combat
timing in ticks, never animation events**; **one action lock, not a stack**; **position is the
feet, overlap is strict, coyote counts ticks**; **three scenes, not four**; **progression is an
`AbilityLoadout` asset**; **levels are composed in lanes**; **Cinemachine, Follow only, no Aim**;
**hitboxes are sim-side, not trigger colliders**; **hit geometry is ours, by separating axis**; **animation is a view of the sim and decides nothing** (`Plans/Animation-Contract.md`); **cover
is decided per attack**; **one definition of solid, two questions**.

Added this session:

15. **A window is start + duration, and the opening tick never moves.** A stat changes only how
    long a window stays open. The opening tick is the cue a player learns off a telegraph, so gear
    makes an answer easier to *hold*, never earlier to *start*. It also removes the centred-window
    rounding problem entirely: only the duration is ever rounded.
16. **Phases are three durations, not three ranges.** Startup, active and recovery are authored as
    lengths that sum to the total, so they tile the action exactly and cannot overlap, gap or be
    malformed. Windows are absolute ranges laid *over* them, because an i-frame window routinely
    straddles startup and active.
17. **Hit timing is fixed; survival timing scales.** Hit boxes carry plain `TickRange`s because
    when the blade is dangerous is the move's identity. Parry and i-frames are `ScalableWindow`s
    because that is what gear should widen.
18. **The timeline decides nothing about damage.** It reports which box is live; something else
    asks the world who is inside it. That seam is what lets the geometry backend change.
19. **Which attack a press produces is data.** Stance names an id and the module looks it up, so
    adding a move — or pointing two stances at the same one — is an asset edit. Proven by a test
    that adds an attack and fires it with no code change.
20. **The combat world is a per-scene seam, not a global.** `ICombatWorld` is handed to the module
    and is re-pointed each tick from `CombatDirector.Instance`, because the player is a persistent
    prefab and the fight it is in arrives with a scene load.
21. **One hit per box, per target, per action** — keyed on the box index, not the attack, so a
    multi-hit move's second sweep connects again. Cleared on every chain link.
22. **Defence is an ordered gate chain, first-wins: i-frames, then parry, then block.** Each answer
    is a rule that may claim an incoming hit or pass it on, rather than a branch in one place. A new
    defensive answer — a counter, a guard-break, an elemental ward — slots in without editing the
    ones already there. Same argument as ability modules never referencing each other, applied to
    the receiving end.
23. **The defender's answer goes back to the attacker.** `OnHit` returns a `HitResult`, and a parry
    reports the stun the attacker should take. A defensive system that only subtracts damage makes
    parrying a slightly better block; the opening is the mechanic. The attacker stuns *itself* from
    its own hit's return value, so nobody reaches across at anyone.
24. **Stance picks the guard, exactly as it picks the attack.** Standing answers high, crouching
    answers low, and the block lock freezes stance while it is held — so committing to a height is
    the decision and switching costs the time it takes to drop the shield. A guard that answers
    everything is a button that makes you invincible.
25. **Unblockable is a per-hit-box flag.** Without it, holding the guard is the correct play against
    every blockable attack in the game and the telegraph is not worth reading.
26. **Blocking shoves, it does not stun.** Parking a stun would hand the character to hit-react,
    which force-locks — and taking the lock takes the guard down, which is guard-break by accident
    on every blocked hit. A guard already suppresses moving and attacking, so blockstun would be a
    duration in which nothing further is prevented.
27. **`ForceLock` exists for things that are not a choice.** `TryLock`'s "higher priority *and* an
    open cancel window" is what makes an attack a commitment, and a hit-react obeying it would never
    interrupt the swing it is a reaction to. Being hit, being parried, dying and scene transitions
    force; everything voluntary does not.
28. **Ducking under a high attack needs no rule.** A simulated hurtbox is built from `BodySize`,
    which is already the crouch height when crouched, so the geometry misses on its own. Only
    *blocking* needs the authored `AttackHeight`.
29. **The down-thrust swings its own blade.** The bounce sat unreferenced for two milestones
    because every obvious caller was a module reaching into another one. The way out was that there
    was never a second module involved: a down-thrust is *one action with a movement half and a
    damage half*, so it resolves its own hit and bounces itself. When a reaction seems to need a
    cross-reference, check first whether the two things are actually one thing.
30. **`ICombatWorld` lives on `CharacterSim`, not per module.** It is the same answer for every
    module that swings, and it changes when a scene loads — one place to re-point rather than one
    per module, and a new attacking module gets it for free.
31. **A dodge's startup is what stops it being a panic button.** Three vulnerable ticks before the
    i-frames open mean the dodge has to be predicted rather than reacted with — the same argument
    the parry's startup makes, and the reason both are readable answers instead of reflex tests.
    The recovery is the other half: an action shorter than its own window would make holding the
    button a permanent i-frame generator, and there is a test for exactly that.
32. **Reactions tick before what they interrupt.** Parry 4, dodge 5, block 6, attack 7. A reaction
    claiming the lock after the thing it is interrupting has already ticked leaves the guard up and
    the swing running for one more tick. The gate chain resolves it correctly anyway and a dead
    swing does no damage — but a character who is briefly parrying and blocking at once stays
    harmless right up until something reads it.
33. **Which answers work is authored per hit box, as a set of what the attack DEFEATS.** Stored as
    negatives so the safe default is zero: Unity is on C# 9 here, where a struct cannot carry field
    initializers, so an "answers that work" field would have defaulted to *none of them* and made
    every attack in the game silently unanswerable. `HitEvent.CanBe(...)` reads the right way round
    at the call sites.
34. **One type serves projectiles and area attacks.** A bolt is a small volume with speed; a
    shockwave is a wide one with growth and no speed. Splitting them would mean two lifetimes, two
    resolution loops and two places for the dedup rule to drift — and the interesting authoring
    space is the middle, which a split makes unreachable.
35. **A spawned volume holds no reference to whoever fired it**, only an id and a team. That is the
    point of it: the caster can be parried, stunned or killed mid-flight and the shot still lands.
36. **Nothing is reflected on a parry.** A parried shot dies where it was turned. Reflection is a
    mechanic this project has not decided on, and inventing it would make a parry behave differently
    against a bolt than against a blade.
37. **Guard and parry are one button, and timing decides.** The first few ticks after the press are
    a parry window; after that the same held button is a block. You cannot hold a parry, only time
    one. A parry ignores guard height — timing beat the attack, and refusing a well-timed deflection
    for being aimed at the wrong part of a shield would make the window a worse block rather than a
    better one. **Consequence worth watching:** a mistimed parry is now just a block, so there is no
    whiff punish for trying — which was previously what made it a read.
38. **A connecting down-thrust hands back the air jump**, through `CharacterState.AirRefreshTick`
    rather than one module calling another. Anything else that should earn a mid-air reset writes
    the same field. It lands one tick later than the bounce, because the jump ticks at order 35 and
    the dive stamps it at 50 — invisible behind the eight-tick arm delay.
39. **Bodies can hurt to touch.** Contact is resolved once, centrally, by the director: two
    participants each running their own overlap test would deal the damage twice. It goes through
    the same `OnHit` as everything else, so a dodge answers a body exactly as it answers a blade.
40. **Holding attack keeps the dive alive through a bounce.** Release takes the pop and hands back
    air control; hold and it keeps falling on things until it lands. The bounce is authored as a
    *height* equal to `JumpHeight`, because a pop that lifts less than a jump makes descending
    anything a losing race with gravity.
38. **Blocked counts as connected; invulnerable does not.** A down-thrust pops off anything solid it
    struck, and whether the target was hurt is the target's business. Phasing through i-frames is
    not a connection, or the dive becomes a way to hover over anything recently hit.

---

### Added 2026-07-31

24. **Animation is a view of the simulation and decides nothing.** No root motion, no
    `AnimatorController` state machine (the sim already arbitrates — a controller graph would be a
    second state machine describing the same thing), and no gameplay vocabulary in
    `ClipEventChannel`. Full reasoning and the interaction-injection contract:
    **`Plans/Animation-Contract.md`**.
25. **An interactable supplies the clip the player performs on it**, plus an anchor. The sim owns
    the lock, the move to that anchor, and the duration in ticks. The clip never decides that an
    interaction finished — that is the animation-event mistake in a different hat. Contract doc,
    §3.

---

### Added 2026-07-31, stats

26. **Three stats, not six, and they are levels rather than scores.** Might, Flame, Heart from
    design bible §6.2, each 1..8, with what a level is *worth* derived through `StatTuningData`.
    A save holds "Heart 4", never "max health 220", so retuning reaches existing saves.
27. **Stat modifiers apply in fixed stages, never in arrival order.**
    `(base + flats) × (1 + percents) + finals`. Every operation within a stage commutes, so two
    machines with the same modifiers compute the same number whatever order they were picked up
    in. HopeFell's applies through a multicast event in subscription order and has a latent
    ordering bug because of it — see `MIGRATION-HopeFell.md`.
28. **Stat durations are absolute ticks.** Same reason as every other window in the project: a
    wall-clock buff lasts a different number of ticks at 30 fps than at 144.
29. **Levelling Heart grants headroom, not health.** A level-up mid-fight would otherwise be an
    emergency heal, which turns progression into a combat resource.

30. **Reapplying a debuff refreshes, upgrades, then caps — in that order.** Every instance in the
    group takes the new expiry, the group is upgraded if the incoming is stronger by magnitude, and
    a further instance is added only below the cap. Default cap is **one**. A weak reapplication
    can therefore refresh a strong debuff but never dilute it, and a whole stack expires together
    rather than drifting apart. Opt-in via `StatModifier.StackKey`; gear leaves it at zero so two
    rings of +1 Might still give +2.
31. **Damage over time is not built, and does not belong in stats.** A DOT applies damage rather
    than modifying a stat, so it belongs beside `HitEvent` and the gate chain — otherwise a poison
    tick would route around i-frames, block and parry. Deferred to the enemy pass, when whether it
    is blockable is answerable rather than guessable.

32. **Speed is modifiable but not levelable, and does not count as power.** Slows and hastes need
    a home, and a second system with its own duration, stacking and expiry rules would be the wrong
    one. So `StatKind.Speed` sits *below* the three progression stats — Resolve cannot buy it,
    `SpendLevel` refuses it, and `TotalLevels` skips it so a haste potion never reads as complicity
    at the finale (§6.3). Anything added below `StatKinds.ProgressionCount` is automatically
    modifier-only.
33. **A restriction is not a stat.** Silence, disarm and root have no value to scale and no
    "strongest" to compare, so `AbilityRestriction` is its own type sharing only the lifecycle. It
    bars either `LockFlags` (the same vocabulary the action lock speaks) or one `AbilityId`.
    **A barred module does not tick at all** rather than ticking and refusing — modules hold state
    across ticks, and one advancing while forbidden to act is how a silence ends with the attack it
    was meant to prevent already half-wound.
34. **Clamping playback speed is foot drift, by definition.** The multiplier is what makes stride
    match travel, so any clamped speed slides by exactly the ratio clamped away. The bounds are
    guards against division nonsense, not tuning — set wide enough (0.05 … 4) that nothing from the
    move threshold to a hasted run touches them. A speed debuff needs no animation handling of its
    own precisely because playback is computed from *actual* velocity.

35. **The AI drives the simulation; it is not part of it.** A brain implements `IInputSource` and
    produces an `InputFrame` — the same struct a gamepad produces — and the sim executes it. The
    test that proves it: hand a player's input to an enemy, or a planner's output to the player,
    and neither the sim nor any ability module notices. `InputSourceTests` asserts exactly that,
    driving a character from a scripted sequence with no scene, no device and no AI.

    **The rule that erodes silently:** a brain that writes `sim.State.Position`, moves a transform,
    or calls an ability directly has stepped over the line, and *nothing will fail*. The game will
    look right and the character will be outside its own rules — no action lock, no i-frames, no
    cover, and nothing a headless test can reproduce. **Actions press buttons. Only ever buttons.**

    The corollary is why enemies are cheap: an enemy is the same `CharacterSim` with a different
    `AbilityLoadout`, so every combat rule already applies to it.

36. **Proving `com.rgs.goap` is game-ready is a project goal, not a means to an end.** So "would a
    state machine be simpler here" is already answered for enemies: GOAP drives them because
    exercising it in a shipping game is part of the point. Do not re-litigate it per enemy.
37. **GOAP lives in its own assembly, `Rokkan.Prophecy.Goap`.** Not in the sim, which
    `SimArchitectureGateTests` would reject outright, and not in the main runtime, which would drag
    Burst and Collections into every consumer. Sensors and strategies are the only things that
    know both worlds.
38. **A GOAP action writes `EnemyIntent` and nothing else.** No position, no transform, no ability
    call — the intent becomes an `InputFrame` and the sim decides what it means, so a planned
    attack obeys the same action lock, cancel window, cover check and i-frames a player's does.
    This is decision 35 applied to the planner, and it is the one that will erode silently: a
    strategy that moved the transform would look right on screen and be outside every combat rule,
    with nothing failing to say so.
39. **Perception runs once per tick on `EnemyBrainHost`, not inside sensors.** Sensors copy the
    result to the blackboard. Sensing twice would let the planner and the body disagree about where
    the target is; sensing on a sensor's own interval would tie an enemy's reactions to a cadence
    unrelated to the tick its combat runs on.

### Stat parity audit vs HopeFell — 2026-08-02

Every member of HopeFell's stats system, checked against Prophecy's. Three rows are deliberate
refusals; everything else has an equivalent.

| HopeFell | Prophecy | |
|---|---|---|
| `StatType` enum | `StatKind` | ✅ different list by design (§6.2) |
| `BaseStats` SO | `StatProfile` SO | ✅ authored levels + innate modifiers |
| `IStatProvider.GetStatOfType` | `IStatSource.Effective` / `.LevelOf` | ✅ |
| `StatStruct` | `float` from `Effective` | ✅ value returned directly |
| `EntityStats` | `StatBlock` | ✅ |
| `StatsMediator` | folded into `StatBlock` | ✅ no separate object |
| `STAT_ADD` | `StatStage.Flat` | ✅ |
| `STAT_MINUS` | negative `Flat` | ✅ |
| `STAT_MULTIPLY` | `StatStage.Percent` | ✅ sums rather than compounds |
| `STAT_DIVIDE` | negative `Percent` | ✅ ÷2 is −50%, without integer truncation |
| `AddModifier` | `Add` | ✅ |
| `AddModifiers(array)` | `AddRange`, `AddSpecs` | ✅ |
| `RemoveModifier(one)` | `RemoveId` | ✅ by id — a struct has no reference identity |
| `RemoveModifiers(array)` | `RemoveSource`, `RemoveSources` | ✅ |
| `MarkForRemoval()` | `Cancel(id)` | ✅ |
| duration timer | `ExpiresOnTick` | ✅ ticks, not wall clock |
| permanent (duration ≤ 0) | `StatModifier.Permanent` | ✅ |
| `Update(deltaTime)` mark-and-sweep | `PruneExpired(tick)` | ✅ |
| `OnDispose` event | `PruneExpired(tick, expired)` | ✅ list out-param — no allocation, no subscriber running inside the tick |
| `StatModifierDetails` | `StatModifierSpec` | ✅ authored duration, resolved at apply |
| `StatScaleStruct` | `StatScale` + `Evaluate` / `ApplyToDamage` | ✅ |
| `ToString` | present on stat, modifier and spec | ✅ |
| arbitrary `Func<int,int>` | — | ❌ **refused** |
| `abstract StatModifier` subclassing | — | ❌ **refused** |
| stats as `int` | `float` | ❌ **changed** |

**The two refusals.** Arbitrary operations and custom modifier subclasses both let a modifier carry
executable behaviour. Neither serialises into a save, neither crosses a wire, and neither can be
checked for determinism — a modifier whose effect is a closure is one two machines cannot agree
about. The three stages express every operation HopeFell actually uses (`ADD`, `MINUS`, `MULTIPLY`,
`DIVIDE`), so nothing real is lost. If a case appears that the stages genuinely cannot express, the
answer is a **new named stage**, not a delegate.

**The change.** HopeFell's `STAT_DIVIDE` truncates, so 7 halved twice is 1 rather than 1.75.
Effective values are floats here and only the final derived numbers round.

### Added 2026-08-03, world transitions

- **Portals are walk-in volumes, and they live in presentation.** Zelda II's transitions are tiles
  you step onto, so crossing the threshold is the input — no interact press. The volume test is
  frame-timed MonoBehaviour code against the sim's mapped feet, exactly like the kill plane: a
  scene load is not replayable state, so the sim gains nothing from owning it and would grow a
  third kind of geometry doing so. The `Interact` seam stays free for doors that should ask first.
- **A portal arms only after the player has been seen outside it.** Portal pairs point at each
  other's spawns, and nothing guarantees an arrival spawn sits clear of a portal volume — the slip
  would ping-pong the player between two loads forever. One clean frame outside before firing
  makes that structurally impossible rather than avoided by careful placement. `PortalTests` holds
  the rule; mid-transition frames neither arm nor fire.
- **Each world scene carries the camera that knows how to shoot it; Cinemachine priority is the
  entire handover.** Every number on `LaneCameraRig` is side-scroll vocabulary — lanes, jump-height
  dead zones, fall look — so the overworld got its own rig rather than a mode flag. It sits in the
  overworld scene at priority 50 over the Bootstrap rig's 0, wins while loaded, and takes the
  answer with it when the scene unloads. Same shape as §11.11's "spatial policy is per-space and
  pluggable", applied to cameras. The rig is self-assembling (adds its own composer, owns its
  priority) so the scene generator never references Cinemachine — the same reason `EnemyBuilder`
  attaches GOAP by reflection.
- **The overworld camera is a fixed overhead, square to the world.** Pitch 50, yaw **0**, FOV 22,
  applied as a constant every frame — Follow only, no Aim, and the map never rotates. Framing
  follows the project rule: the authored number is the character's viewport share (0.13 — a token
  on a map, not the subject of a shot), and the distance is derived (35.6 m).
  **Revised 2026-08-04 from a Tunic 45° yaw:** the diagonal looked handsome and played wrong —
  input is world-axis-aligned, so under a yawed camera every press walked the hero diagonally
  across the screen. A camera angle must never make the controls lie. The yaw field survives for
  authored set-pieces; the default is straight-on.
- **World transitions cut, never blend.** The brain's default blend was EaseInOut 2 s, which on a
  scene swap swoops the camera across the world between the two rigs — the cross-camera version of
  the slide `SnapToTarget` exists to prevent. Set to Cut in Bootstrap. If a camera pair ever
  genuinely wants a blend (the finisher shot), a custom-blends asset can name exactly that pair.
- **Leaving by different ends arrives at different spawns.** Both traversal portals lead to the
  overworld, but west arrives west of the return cube and east arrives east — the level occupies a
  place on the map rather than being a menu entry.

---

## 4. Traps already hit — do not rediscover

Carried forward, still true: **both `.gitignore` files are required**; **tests in `file:` packages
need `testables`**; **`-batchmode -runTests` refuses a project an Editor has open**; **do not
regenerate the shared `Sim` `.meta` files**; **two red `[RandomSource]` lines are HopeFell's own
fallback test**; **`runInBackground` is on for tooling-driven play tests**; **a failed
`Unity_RunCommand` exits play mode, so check `Application.isPlaying`**; **Cinemachine ≤ 3.1.4 does
not compile on 6000.5**; **prefab asset references can silently fail to persist**; **a prefab
cannot reference a scene object**; **`NewScene(..., Additive)` throws over an untitled scene**;
**camera bounds and offsets write the same number**; **`GenerateContacts` rejects a zero contact
distance**; **rotation makes box-overlap intuition unreliable**.

New this session:

16. **Module tick order is load-bearing in two directions at once, and both cost a real bug.**
    `Crouch` had to move *before* `Attack` (stance chooses the attack, so a stale stance throws a
    chest-height slash on the tick you press down and attack together) and `Attack` had to stay
    *before* `GroundMove` (an attack drops its lock the tick after its last authored tick, so
    running later hands control back one tick late). `Crouch = 2`, `Attack = 5`.
17. **A lock is not a released stick.** `Crouch` folded `Can(LockFlags.Move)` into its "is the
    player holding down" test, so the swing's own Move lock read as letting go and the character
    stood up on tick two of their own low thrust. Anything that gates *state* on a lock must treat
    the lock as "frozen", not as "input absent".
18. **Injected input needs a real release between presses.** Queueing two `KeyboardState(Key.J)`
    events with no empty state between them produces no second press edge, because `ButtonLatch`
    derives edges from the level sampled once per rendered frame. Two arena checks silently
    "passed" by never swinging at all before this was caught. Release, let a frame pass, then
    press.
19. **A double-counted origin is invisible in the scene view.** The arena's raised dummy put its
    root at the top of its pedestal *and* offset its hurtbox by the same height, so the volume sat
    a pedestal above the box you could see. Heights are measured from the floor; only geometry
    moves.
20. **Comparing a double accumulator against `SimConstants.FixedDeltaSeconds` drifts.** The float
    constant is a hair above `1.0/60.0`, so a 60 fps frame falls just short of a tick and the
    baseline run retires 89 ticks instead of 90. Derive both from `TicksPerSecond`. Related: an
    fps-invariance test must compare a fixed number of *ticks*, not a fixed number of frames — how
    many ticks a wall-clock second turns into can differ by one at the tail, and that is the
    clock's arithmetic rather than anything about combat.
21. **Unity does not serialize readonly fields, and says nothing about it.** Hit windows were a
    `TickRange` field on `AttackHitBox`; `TickRange` is a `readonly struct` with readonly fields, so
    every window round-tripped through the asset as `[0..0)` — which reads as "no window". The
    project looked fine only because the freshly-created in-memory instance kept its field
    initializers; on the next clean Editor launch **no attack in the game would ever have
    connected**. Authored data is now primitives (`OpenTick`/`CloseTick`) with `TickRange` derived,
    and `AttackModuleTests.TheMovesetSurvivesUnitysSerializer` asserts a real round trip. Any
    `[Serializable]` type with readonly fields is the same bug waiting.
22. **`PlayerInputCapture` clears every latch on focus loss, so tooling cannot hold a button.**
    `OnApplicationFocus(false)` → `ClearLatches()` is correct for a real player, whose buttons may
    have been released while nothing was watching. But an Editor driven over MCP never has focus, so
    *held* state is permanently zero — press edges survive because they are set and consumed inside
    one frame, held state cannot. Two arena checks "passed" by never blocking at all before this was
    spotted. To verify held input, remove the capture component and push an `InputFrame` straight
    into the sim; everything below the capture layer is what is under test anyway.
23. **`Unity_ReadConsole` can report zero errors while the build is broken.** Three compile errors
    sat in `Logs/Editor.log` while the MCP console returned nothing and `IsCompiling` read false —
    the only symptom was assemblies that quietly stopped being rebuilt. **Check the DLL timestamps
    in `Library/ScriptAssemblies/`, and grep the Editor log for `error CS`**, before believing a
    clean console. `CompilationPipeline.RequestScriptCompilation()` forces the rebuild.
24. **`result.LogWarning` in a `Unity_RunCommand` is reported as a failure** — and a failed command
    exits play mode. Use `Log` for anything that is not genuinely an error.
25. **Struct field initializers do not compile here.** Unity 6000.5 is on C# 9. Any default on a
    `[Serializable] struct` field has to be expressed as "zero means the sensible thing", which is
    worth designing for rather than discovering.
26. **Three copies of a dedup rule is three chances for them to disagree.** The attack module, the
    training attacker and the down-thrust all need the same five steps around a hit. The third copy
    was the moment to extract `HitSweep`; it should have been the second.
25. **Two tile pieces may never place same-facing surfaces on the same cell-boundary plane.**
    The cap's slab sides sat exactly on the boundary plane — the same plane, same facing, as the
    cliff face fronts covering that edge — and the cap's dark edge boiled through the face as a
    stippled band whenever the camera moved. It looked like a lighting bug and survived turning
    shadows off entirely, which is the diagnostic: view-dependent stipple that ignores lighting
    is coplanar geometry, not light. The rules now baked into the tile generator/planner: cap
    sides inset 0.02 m inside the boundary (the covering piece owns the plane alone); stacked
    faces/posts step 0.01 m toward the low side per tier (an upper piece's skirt shares the
    plane of the front below it); post rims ride 0.02 proud of face rims; and mirrored pieces
    are baked twins, never negative scale (which reverses winding and renders inside-out).

24. **Arming and advancing a timeline in the same tick, twice, moves every window one tick early.**
    `Parry.TryStart` armed and advanced so the action's tick zero is the tick the button was pressed
    — then fell through to the shared `Advance` below it. The parry window opened at elapsed 1
    instead of 2, which is a different move from the one that was authored. Caught only because the
    window is asserted tick by tick rather than at its edges.

---

## 5. Tooling

**Unity's own MCP (`mcp__unity-mcp__*`)** is registered and connected. `Unity_RunCommand` compiles
C# in a dynamic assembly that **cannot reference project assemblies** — resolve project types by
reflection over `AppDomain`. It *can* reference `UnityEngine.InputSystem`, which is what makes
driving a play-mode input test possible at all (see trap 18). Its logger does **not** honour format
specifiers — pre-format with `string.Format`.

**Generators** (`Prophecy > Build > …`): `Create Missing Data Assets`, `Generate Input Actions`,
`Generate GrayBox_Traversal`, `Generate GrayBox_Arena`. All idempotent, all also `-executeMethod`
targets. **Tests**: `Prophecy > Tests > Run EditMode Tests` writes `Logs/test-results.txt` — totals,
any failures, and a per-fixture breakdown (added 2026-07-30, so §2's numbers can be refreshed by
reading a file instead of counting attributes).

---

## 6. Movement, camera and now combat numbers are provisional

Movement and camera were judged **"good enough to move on"** and never felt against real art. The
combat numbers are one step behind that: they were **derived, never felt at all**. Startup is long
enough to read as a telegraph, recovery long enough that a whiff costs something, and the cancel
window opens partway through recovery — but no one has played it yet.

Defence is newer still and even less felt. The numbers most likely to be wrong, in order: the
**parry window** (6 ticks — 0.1 s, which is tight for a first pass), the **telegraph startup**
(34 ticks, the number that decides whether a fight is fair), **`ParryStunTicks`** (40 — the entire
reward for a correct read, and the difference between a parry that buys nothing and one that ends
a fight), and **`BlockedDamageFraction`** (0.25, the clock a turtle is on).

The whole point of `CombatTuning.asset` is that these move. Do not build anything that depends on
an exact current value surviving; derive from the asset the way `GrayBoxArenaBuilder` does.

---

## 7. Known gaps

### The Overworld Grid's remaining edge cases (enumerated 2026-08-05, on Matt's "unless you can name more?")

With rivers, roads, bridges, caves, overhangs and layered transitions done, what the model still
cannot say — each deliberately deferred, none blocking:

- **Elevated water — CLOSED 2026-08-05, same day.** Water levels inherit from the terrain
  along the course, monotonically downstream; waterfall sheets hang wherever water meets lower
  water. What remains open inside it: the sheets are static quads (no scroll/foam — a shader
  pass, which Matt has flagged as the fords-and-cutaway shader conversation), and a lake still
  needs a river-polyline to carve it (an authored lake footprint would be one more noun).
- **Fords.** Sea refuses; there is no walkable shallow water. A ford is one new cell notion
  (Ground that renders wet) — cheap when wanted.
- **Overlay ramps.** The second surface is FLAT by design, so no stairs INSIDE a cave, no
  sloping bridge, no deck at a height its access does not already reach. The recorded cap;
  build with the feature that needs it.
- **A third surface.** One overlay per cell — a bridge over a road over a canal in a gorge
  (three walkable floors) exceeds the model. No authored place wants it yet.
- **Roof cutaway.** The overhead camera cannot see INTO an enclosed cave (the terrain roof
  occludes the floor). The natural next presentation slice: hide roof caps when the player's
  layer says beneath.
- **Roads on slopes.** Road paint skips ramp/stair cells (an ALttP road breaks at a stair —
  arguably correct forever).
- **Wanderers and layers.** The touch gained a height gate, but wanderers themselves never walk
  decks or caves (NavMesh bakes the base walkable tops; the deck caps ARE in the bake, so they
  could route across a bridge — but nothing spawns them there deliberately).
- **River mouths.** A river meeting the coast composes from the same shore pieces as everything
  else; there is no distinct delta/estuary read. Cosmetic only.

*(The old list had grown two competing numberings and still opened with "no dodge", which has been
built since. Renumbered and re-checked against the code on 2026-07-30.)*

1. **No air dodge.** Grounded only, because it changes what jumping commits you to and that is a
   feel decision worth making deliberately rather than inheriting.
2. **The down-thrust's hit box ignores its own window.** `AttackHitBox` carries `OpenTick`/
   `CloseTick` because every other volume needs them, but the dive lasts until it connects or
   lands, so no tick count could describe it. The fields sit unused and are documented as such —
   worth revisiting if a second unbounded volume ever appears.
3. **Top-down collision — CLOSED 2026-08-04, grid-backed.** The sim gained a ground seam
   (`ITopDownGround`: `CanStep`/`HeightAt`, plain C#, null = free movement), consulted by
   `Integrate`'s top-down branch with the same axis separation side-scroll uses, testing the
   body's leading edge. The overworld answers it from `OverworldWalkGrid` — the Stålberg grid's
   all-floor quads rastered once at load into 0.5 m cells, so collision and tiles derive from
   the same source and cannot disagree. Coast partial-quads count as sea (collision one tile
   tighter than the art, the safe direction); steps above 0.35 m refuse both ways, so terraces
   are cliffs and authored ramps need no new rule; a body stranded off-floor may always step
   back on. Published via `TopDownGroundSource` (the `CombatDirector.Instance` pattern),
   re-pointed by every host per tick — wanderers respect coasts too. `TopDownGroundTests` ×8.
   NavMesh was considered (the worldgen package supports it) and declined for movement: engine
   queries against baked data break the headless contract — the same argument that rejected
   PhysX raycasts. **The agreed shape for when routing arrives (Matt, 2026-08-04): NavMeshAgent
   as a steering oracle, never a mover** — `updatePosition`/`updateRotation` off, the agent
   computes `desiredVelocity` along its path, a GOAP strategy reads that direction and writes
   `EnemyIntent.MoveX/MoveY`, and the sim stays the only thing that moves anybody. Routing
   through the agent, actuation through the buttons — decision 38 survives with pathfinding
   attached.
   **BUILT 2026-08-05 ("tie the Wanderers to the nav mesh"): `NavSteeringOracle`** (main asm,
   Presentation) — adds the hidden agent, syncs `nextPosition` to the sim-moved body per
   frame, and `Route(heading)` paths a 6 m LOOKAHEAD of the desired heading (SamplePosition
   radius 4 — a point wished into a cliff face resolves to the terrace above, which is how a
   ramp becomes worth walking toward) and returns desiredVelocity's direction at the input's
   magnitude. Deliberately a lookahead and NOT the target: routing to the target would turn
   the drunken menace into a homing missile — the player-bias stays in `RoamSteering`'s roll,
   the oracle only answers "which way is that, along ground that exists". EVERY failure
   (off-mesh, unsampleable wish, pending path) returns the caller's heading unchanged, so
   headless tests and side-scroll bodies never notice it. `RoamStrategy` routes through an
   oracle when the body carries one; the overworld SPAWNER attaches it (the spawner is the
   thing that knows this world has a bake — side-scroll enemies never get one). Rode along:
   `CarveUnwalkables` (see props gap above) because the mesh became a steering authority the
   moment the oracle shipped.

   **THE THREE CUTAWAY STYLES (Matt, 2026-08-07)** — the deferred camera/shader conversation
   landed: (1) HALO cutout for walking behind things, (2) INVERT cutout for caves (world goes
   black, the covered room is revealed), (3) CAMERA CUT zones that re-orient to display
   something. Decisions: build order invert → halo → camera cut; cave-vs-bridge is a
   `CoverStyle` flag on the layer noun (Auto derives from geometry — floor below terrain =
   Cave, deck above = Bridge — override for overhangs), bridges additionally get a deck halo
   in slice 2; camera cuts ride a CINEMACHINE MIGRATION of the overworld rig (Matt chose the
   bigger rebuild over a pose-override stack).
   **Slice 1 BUILT (2026-08-07): the INVERT cutout.** Compiler: `SetOverlay` resolves cover
   style at stamp time, `BakeCoverRegions` flood-fills connected cave cells into rooms
   (`CoverRegionAt`, 255 cap); `OverworldCoverLut.Bake` = R8 room mask;
   `OverworldCoverRules.ActiveCaveRegion` = the one runtime decision (on the covered FLOOR,
   not the roof — RoofClearance 1 m), all headless-tested (825 green). Builder: LINEAR R8
   mask texture, roof pieces bucketed per room at Place time (OwnerCell in room + y above
   floor + 1 m), pruned on chunk rebuilds. Runtime: `CaveRevealDriver` (host-attached, play
   only) hides the active room's roof and fades `Prophecy/CaveInvert` — a FullScreenPass
   renderer feature on PC_Renderer that reconstructs world XZ from depth and blacks
   everything outside the room's mask. The Foothills Hollow needed ZERO touches: Auto
   derived it Cave. **FOUR TRAPS, paid for live:** texture GLOBALS never reach a RenderGraph
   fullscreen pass (scalar globals do) → bind through the pass MATERIAL, loaded via
   Resources; `new Texture2D` defaults to sRGB, which crushes an R8 id mask to ~zero → pass
   linear:true; a shader uniform WITHOUT a Properties entry silently refuses
   Material.SetTexture → the Properties block is load-bearing; UNITY_MATRIX_I_VP in a
   fullscreen pass is the blit's matrix, and the camera inverse VP must be handed over with
   GL.GetGPUProjectionMatrix(renderIntoTexture: TRUE) — false "almost works" and smears the
   mask along view-Z. Tool: Layer selection panel gained the Cover dropdown.
   **Slice 2 BUILT (2026-08-07, same day): the HALO cutout.** `HaloCutout.hlsl` — a
   world-space cylinder along the camera→player axis; fragments inside the radius AND
   nearer than the player (margins both ends, so their footing and back wall stay whole)
   are Bayer-4×4 dither-discarded with a soft edge in the outer 40%. Cut in ForwardLit AND
   DepthOnly (depth must agree with color or depth-reading effects see ghosts of cut
   geometry), NEVER ShadowCaster (the hole is a courtesy to the camera, not a hole in the
   world — light does not leak). Lives in TWO shaders: `Prophecy/GrayBoxLit` (new — guide
   texture/plain colour matte lambert, replaces URP Lit on ALL tile and gray-box materials
   via the generators' idempotent set-every-run) and `Prophecy/OverworldGround` (bridge
   DECKS are caps, so the see-the-player-underneath hole is the ground shader's job — the
   deck-halo-under-bridges half of Matt's pick came free). `HaloCutoutDriver` (host-attached)
   feeds the globals and zeroes on disable, so side-scroll scenes wear the same shader
   inert.
   **REWORKED THE SAME NIGHT after Matt drove it — the shipped design differs from the
   first cut in four load-bearing ways:**
   (1) **The INVERT moved INTO the geometry shaders** (`CaveInvertMask.hlsl`, applied in
   ForwardLit of both gray-box shaders; the fullscreen pass, its material, its renderer
   feature are DELETED). The depth-reconstruction pass died of an empty depth texture after
   the world moved onto our own shaders — root cause never pinned, and it never needed to
   be: geometry fragments know their world positions exactly. The void beyond the world's
   silhouette is the CAMERA BACKGROUND, faded to black by the reveal driver (clear flags
   stored/restored, including on teardown). No matrices, no per-camera hooks, no RenderGraph
   binding rules. Wanderer-shaped caveat: anything on a STOCK shader outside a room stays
   lit during a reveal (only our shaders carry the mask) — irrelevant today, remember it
   when new shaders arrive.
   (2) **The halo's cut volume is a CONE, not a cylinder** (Matt's shape): apex at the
   camera, opening through a player-sized disc (radius 0.75 m) at the player's depth, far
   cap just before the anchor. A fragment is cut only if ITS view ray strikes the disc —
   walls BESIDE the player stay whole (the cylinder drilled smudges through every wall edge
   walked past), while lids, canopies and cliff faces that truly screen the body always cut.
   (3) **The halo cuts BOTH faces and never gates on geometry heuristics.** GrayBoxLit is
   Cull Off; backfaces shade near-black (the world's inside — a hole shows dark rock, not
   the under-terrain water plane), but backfaces INSIDE the cone are cut like anything else:
   exempting them sealed the player behind the first shell's interior ("only cutting the
   first thing it hits"). An above-the-head-only rule was tried and reverted — it starved
   legitimate reveals into slits.
   (4) **The halo is EARNED via `OverworldSightline`** (pure, tested): the grid marched
   camera→chest — terrain mass, greeble/unwalkable cells carrying a canopy-height mass
   (2.5 m), deck slabs by segment straddle. The sightline is deliberately LIBERAL (no
   hugging exemptions — one hid the player behind hugged walls entirely): with a surgical
   cone, a false trigger cuts nothing visible.
   Also: roads wear GrayBoxLit (they must black out in reveals and take the halo);
   billboard-disc treetops were tried and REVERTED (interlocked discs read worse than
   blobs, and the cone sees the player through a sphere canopy anyway). The halo cuts
   ForwardLit ONLY — never ShadowCaster (light must not leak) and never DepthOnly (cutting
   depth there emptied the depth texture pipeline-wide once; SSAO ghost-depth in holes is a
   gray-box shrug). Matt's verdict after driving all of it: "that fixed everything."
   **Slice 3 BUILT (2026-08-07): CAMERA CUT zones — and the "Cinemachine migration" turned
   out to already be reality** (OverworldCameraRig has been a CinemachineCamera + Position
   Composer with priority handover all along; nothing to migrate). `CameraCutZone`
   (Presentation): a feet-in-volume trigger resolved like a portal — OBB via
   InverseTransformPoint, no colliders — that raises a posed shot vcam's priority (rig 50 →
   zone 150) while the player stands inside, per-zone blend seconds (0 = hard cut; the
   brain's default blend is set per transition and RESTORED on teardown). The shot is
   authored AS A CAMERA — its transform and lens are the framing — and the zone
   SELF-ASSEMBLES one from serialized pose numbers when none is wired, so scene generators
   stay Cinemachine-free (the rigs' own pattern). `SetHeld(bool)` is the interaction-system
   hook for later conversation/lever shots. Demo: CameraCut_EastwaterBridge in the overworld
   builder — crossing the bridge eases 0.7 s into a low yawed river vista (the rig doc's
   sanctioned set-piece yaw), stepping off hands back. Verified live both ways. Authoring
   note: overworld zones are scene-side for now; a map-tool noun (AuthoredCameraZone) is
   future work if the real overworld wants many of them.

**COMBAT AI TESTER (2026-08-07, after the cutaways):** Matt imported the Iron Roc Warrior
(Meshy, `Assets/MeshyImports/Iron Roc Warrior_*` — UNTRACKED like the hero's T-pose figure;
the prefab reference lives on Matt's machine, which is the only machine) as the placeholder
bad guy. `EnemyModelInstaller` ("Install Roc Model On Grunt") mirrors HeroModelInstaller
onto `Enemy_Capsule.prefab`, plus one improvement: it flips the FBX importer to HUMANOID
itself and reimports before asking for the avatar (the hero's install once stalled on that
manual step). Scaled to StandHeight exactly — NOT bigger for menace, because the hurtbox is
sim-side and an oversized model lies about where it can be hit. Locomotion/attacks come
from the shared `BodyAnimationSet` retargeting onto the Roc's humanoid rig; a Meshy walk
clip sits beside the model unused, future flavor. `GrayBox_CombatTester` (generated,
"Generate GrayBox_CombatTester"): a FLAT featureless sparring floor — no ledges, no
stations, nothing that could excuse the AI — with the player spawn and a
`CombatTesterRespawner` (deliberately dumber than the encounter spawner: a metronome, not a
menace) standing a fresh grunt up 2.5 s after each death. Verified live: the Roc spawns,
closes and swings on its own, model animates via retargeted clips. OPEN QUESTION from the
first live frame: the HUD logged five 10-damage slash_high hits on the player while player
health stayed 100/100 — either the readout or the damage application wants a look while
Matt drives the fight.

   **Wanderers are back ON (2026-08-05) — safety became an authored property.** They shipped
   disabled 2026-08-04 because an idle player was hunted on an eleven-second fuse; now the
   spawner reads the player's cell's PROVINCE (table, cadence, cap) and the provinces around
   the arrival spawn have empty tables, so authoring passes stay undisturbed while the Mesa
   Moor hunts. The menace exists exactly where a province table says it does.

   **NavMesh integrated (2026-08-04, later the same day) — and it is the DEFAULT ground.** The
   grid host bakes a runtime `NavMeshSurface` over the placed tiles (render meshes; the stripped
   colliders are not needed) and `NavMeshGround` answers the seam: floor = `SamplePosition` with
   a tight horizontal tolerance (else the coast answers for the sea beside it), step legality =
   `NavMesh.Raycast` connectivity. A `GroundAuthority` toggle on the host flips between NavMesh
   and the walk grid — both are always built, and the sim cannot tell them apart.
   **Ramps and height-riding landed the same day, and the FlatWorldCeiling is already gone.**
   `OverworldMap` gained `Ramps[]` (start/end XZ, start/end Y, half width) — painted as
   `Region.Corridor` with a Linear Y after the regions, so a ramp cuts its grade into whatever
   terraces it spans. Presentation rides `HeightAt`: `CharacterView` and `OverworldCameraRig`
   fill the railed axis from the ground in top-down, so climbing a ramp raises body and frame
   together while the sim stays flat-planar. Seven ramps authored, one per elevation change.
   **Two traps for the record:** (1) the default 45° bake climbs the tiles' skirt geometry and
   makes every cliff decorative — the bake is slope-limited to 25° so authored ramps are the
   only way up; (2) `NavMesh.CreateSettings`/`GetSettingsByID` return STRUCT COPIES, so a
   NavMeshSurface pointed at "custom" settings silently bakes with defaults — the bake now goes
   through `NavMeshBuilder.BuildNavMeshData`, which takes settings by value (and dropped the
   `Unity.AI.Navigation` package dependency as a bonus; it is all engine-module API).
   Verified live: ramp foot/mid/top climbable, cliff faces refuse, sea refuses, mid-ramp height
   0.40 riding through the view.

   **The map went RECTANGULAR (Matt, 2026-08-04): the target grammar is A Link to the Past.**
   Clear wall/ramp boundaries, every edge an exact tile edge. `OverworldMap.Topology` chooses
   Organic (Townscaper) or Rectangular (the ALttP lattice — `RectAxisAligned`, jitter ignored,
   `Spacing` = tile size, currently 2 m). No system change, only authoring: same regions, ramps,
   collision seam and NavMesh, and the per-region `Structured` toggle simply has nothing left to
   inject in a fully-square world. All collision probes re-verified on the lattice; rotated
   region footprints rasterize into staircase coasts, which reads correctly Zelda. What tile art
   will want next: distinct cliff-face and ramp/stair tiles (the package's per-region tile-set
   variants are the pending schema for exactly this).
4. **The overworld is a proving-ground, not a place.** The scene exists (§2a) but holds nothing to
   do except leave, and "last overworld position" (design bible §9) is not stored — portals name
   fixed spawns, so re-entering does not return you to where you stood.
5. **`AttackModule.Modifiers` is settable but nothing feeds it.** Stats now exist and drive damage
   and max health, but not yet the *window* scales — `IFrameScale`, `ParryScale`, `CancelScale`
   still resolve at 1. Those are gear's job rather than a stat's, and no gear exists.
6. **`Interact` produces a request and a probe box that nothing consumes.**
7. **F1 and F2 overlays ship visible**, gray-box loadout has everything on — release checklist.
8. **Hit geometry is deterministic within an architecture, unproven across.** `HitResolver` is
   our own separating-axis test over `DeterministicMath`, so there is no PhysX left to vary — but
   x86-64 and ARM have not been compared. Harmless for co-op PvE (§10); it would matter for replays.
9. **`RequiredStance` is enforced on start but not on chain links.** Crouching mid-combo does not
   stop a stand-only follow-up from firing.
10. **Death is a respawn, and that is a placeholder.** Running out of health puts the player back
    at the spawn point restored, exactly as falling off the level does. Nothing plays, nothing is
    lost, no encounter ends. It exists so combat can be lost and retried while tuning; the real
    rule is a design decision nobody has made, and it belongs with the Protector fights alongside
    the health-economy question the finisher model already raised. `SceneDescriptor.RespawnOnDeath`
    is the switch to turn off when there is a real one.
11. **Defensive state is read as of the defender's last completed tick.** A hit is resolved during
    the *attacker's* tick, so a guard raised on the same tick the blow lands is not yet up. It is a
    deliberate at-most-one-tick lag — the alternative makes the answer depend on which character
    registered first — but it is a real tick of lenience the player never sees and it should be
    remembered before parry windows are tuned tight.
12. **The build-order registration hole is fixed but only proven headlessly.** `Combatant` used to
    give up permanently if no `CombatDirector` existed when it enabled, which in a build is always
    — Bootstrap is first and the world arrives later, so the player never became hittable while
    still being able to deal damage. `CombatDirector.Start` now sweeps for combatants already in
    the world, and `Combatant.JoinFight` is idempotent. Two EditMode tests cover the mechanism;
    **the end-to-end scene path was not confirmed in play mode** — the console bridge would not
    surface runtime logs during the attempt. Press Play from `Bootstrap`, warp to the arena, and
    check the player takes a hit before trusting it.
13. **`HurtboxSet.Query` is not re-entrant.** One dedup stamp per set, three callers sharing it.
    Each currently reads its results into its own list before doing anything that could query
    again; the next caller has to keep that true. Documented on the method.
14. **`TrainingAttacker` picks a facing within 16 m and no further.** A bounded search replaced a
    scan of every hurtbox in the level (§10). Correct for an arena; when enemies get real
    perception this number goes away with the class.

---

## 8. M4 — done, and what is next

Goal: **stance combat that feels like Zelda II, timed in ticks, testable headless.**

### Done

- **The full window decomposition**, which is what made M4 real work rather than a port. Startup /
  active / recovery as a partition, plus absolute-tick ranges for hit windows (plural), i-frames,
  parry and cancel. This is more than HopeFell ever had — its `AttackTimeline` carried one on/off
  hit window and left parry and i-frames in seconds accumulated in `Update()`.
- **Hit resolution** behind a resolver seam: separating-axis overlap, per-attack cover through
  the same baked geometry movement uses, team and self filtering, and a broadphase in front of it.
- **Combos** with input buffering, gated on the cancel window the arbiter already understands.
- **The module**, registered in `PlayerCharacterFactory` alongside every other ability.
- **The acceptance test**: identical combat at 30, 60 and 144 fps, driven through the real capture
  path — button sampled per rendered frame and latched, accumulator releasing whole ticks.
- **The arena demo** (§9), verified end to end in play mode.
- **Defence, all in ticks** — block by stance, parry as a window on its own timeline, i-frames as a
  damage gate, knockback and hit-react at `LockPriority.HitReact`, all behind a first-wins gate
  chain. **This is exactly where HopeFell stopped** — their parry was `0.2f` seconds accumulated in
  `Update()` — so there was nothing to port and every reason not to reach for seconds.
- **The player as a target**: health on the sim, a hurtbox that follows the simulated body and
  shrinks when it crouches, and hits routed through the gates.
- **Something that swings back.** `TrainingAttacker` runs an ordinary `AttackDefinition` on the
  same timeline and resolver the player uses, on a fixed tick cycle. Three of them in the arena
  demand three different answers.

- **The down-thrust's damage half**, and the bounce it drives. It resolves its own hit through the
  shared `HitSweep` and bounces itself, which is the answer to the cross-reference question that
  had kept `Bounce()` unreferenced since M2.

- **The dodge step**, the only thing in the game that is intangible on purpose. Distance authored,
  speed derived, i-frames covering exactly the active phase behind a vulnerable wind-up.
- **Losing.** Running out of health respawns the player at the scene's spawn point with every stat
  restored, sharing `SceneDirector.Respawn` with the kill plane so the two cannot drift.
  Scaffolding, not design — a defensive system you cannot lose to is one you cannot test.
- **The held down-thrust**, the answer matrix, and volumes that outlive their swing: projectiles
  and area attacks on one type, spawned from an authored tick of any attack.
- **Scale, and the geometry to go with it.** `ImmediatePhysics` replaced by our own separating-axis
  test, a broadphase in front of every damage path, and the fight itself moved out of presentation
  into a plain-C# `CombatState`. Not scope creep — a hundred targets and four co-op players is the
  brief, and `CombatState` is the thing a host would replicate. §10.
- **A body, animating off the sim.** A rigged Meshy hero on the player prefab, 25 Synty clips, and
  a state layer that speed-matches playback to actual velocity so the feet do not skate without
  root motion. `Plans/Animation-Contract.md` is the constitution; `Plans/Animation-Requirements.md`
  is the shopping list and the gap register.
- **Stats.** Might, Flame, Heart and a modifier-only Speed, with a deterministic stage pipeline,
  tick-based expiry, stacking rules and ability restrictions. Audited member-by-member against
  HopeFell's (§3) and built rather than ported.

- **Enemies — done 2026-08-03.** Four archetypes, one brain format, all planner-driven:

  | Archetype | Actions | How it fights |
  |---|---|---|
  | Grunt | Patrol, Pursue, Swing | The full loop |
  | Chaser | Patrol, Pursue (holds station) | No attack — contact damage is the weapon |
  | Ambusher | Lurk, Spring | Aims once and commits; sidestep beats it |
  | Caster | Lurk, KeepDistance, Fire | Retreat *is* the attack; throws a 9 m/s bolt |
  | Wanderer | Roam | The overworld's menace: two-axis meander, biased toward you; touch is the threat |

  The AI drives the sim through `IInputSource` — the same `InputFrame` a gamepad produces — so a
  planned attack is subject to every action lock, cancel window, cover check and i-frame a player's
  is. **"Dumb" is a smaller action set, not different machinery**, which is what keeps one brain
  format, one sensor set and one debugging story across the roster.

  Enemies get their own `CombatTuning_Grunt` (24-tick wind-up vs the player's 6) and an explicit
  `AbilityLoadout_Enemy` — an empty loadout means *every* module on, which silently gave enemies
  the player's double jump, dodge and down-thrust. `AttackTelegraph` renders the startup phase so
  the defence can be timed, and `ProjectileView` renders bolts, which nothing outside the F2
  overlay had ever done.

### Next

1. **The combat orchestrator** (§11.11). The roster works; what it does not do is fight *as a
   group*. This is now the thing between "it works" and "it plays", and it decides §11.10 with it.
2. **Finishers and the camera takeover** — both decided, neither built. A Doom finisher is one-way
   and expressible as an `AttackDefinition` plus a precondition and a reward; it still needs an
   executable/staggered state on enemies, and **a parried attacker is now exactly that state
   waiting to be named**. The last-kill camera shot is nearly free in Cinemachine (a second camera
   at higher priority) but needs an encounter concept that does not exist.
   **The slow-motion trap stands: scale the clock's input, never `FixedDeltaSeconds`, or every
   authored window in the game silently rescales.**
3. **A real death rule**, decided together with the health economy — see gap 11 and the release
   checklist. The placeholder is fine for tuning and wrong for shipping.
4. **Feel.** None of the combat numbers have been played. See §6.

None of these is blocked on anything. Enemies were the thing that unblocked the rest, and they now
exist — finishers have a real attacker to stagger, a death rule has an encounter to end, and feel
can finally be judged against something that fights back rather than a dummy on a timer.

---

## 9. The arena demo — how to play it

**Open `Assets/_Prophecy/Scenes/GrayBox_Arena.unity` and press Play.** `BootstrapLoader` pulls
Bootstrap in on top and the `SceneDirector` adopts the arena, so there is nothing else to set up.

**Controls:** `A`/`D` move, `S` crouch, `Space` jump, **`J` attack**, **`K` guard**,
**`Ctrl` dodge**. Guard and parry are the same button: a hit in the first few ticks after the press
is a **parry**, after that it is a **block**. Airborne, hold `S` and press `J` for the
**down-thrust** — and *keep holding* `J` to stay in it through every bounce. `F1` movement overlay,
`F2` combat overlay.

`L` is still bound to a Parry action that no module reads. Harmless, and left in place because the
binding costs nothing and removing it means regenerating the `.inputactions` asset.

**Number keys 1-0 warp between stations**, restoring health on arrival. The arena is 130 m long and
the stations that fight back are at the far end; without this, testing a parry meant a long walk
before every attempt. The list is in the F2 overlay.

A neutral dodge steps backwards; holding a direction steers it. It does not turn you round, so a
back-dodge keeps the enemy in front of you.

The **F2 overlay** is the point of the demo. It draws the current attack's frame data one cell per
tick — phases on the top row, hit/cancel/i-frame/parry windows on the bottom, playhead through
both — plus hurtboxes, the armed attack's boxes (bright when live), and the cover ray, all in the
Game view via `GL` rather than gizmos. Combat timing is invisible otherwise: a swing that misses
can miss four different ways and they look identical at speed.

**Twelve stations, left to right, every distance derived from the authored hit boxes.** Number
keys warp to ten of them — 2/3 and 11/12 share a marker because they are a pace apart:

| Station | What it asks |
|---|---|
| 1 `Basic` | does anything connect at all — both attacks reach it |
| 2 `Squat` | too short for the high slash; only the crouching thrust lands |
| 3 `Raised` | above the thrust's ceiling; only the standing slash lands |
| 4 `Cover` | inside the hit box, behind a grate — `StoppedByGeometry` refuses it |
| 5 `Chain` | 240 HP, enough to survive the opener so the follow-up has a target |
| 6 `Ledge` | a lane up, so vertical reach gets checked too |
| 7 `High` | swings back, high — hold `K` standing, or tap `K` on the beat to parry |
| 8 `Low` | swings back, low — the standing guard does not answer it; crouch first, then guard |
| 9 `Unblockable` | swings back and no guard answers it at all — move, or parry |
| 10 `Pogo` | a ledge and three tough targets: dive, bounce, and hold to keep pogoing |
| 11 `Bolt` | fires a projectile every ~190 ticks. Every answer works on it |
| 12 `Shockwave` | drops a spreading area at its feet. Only i-frames answer it — dodge or leave |
| 13 `Grunt` | the first thing here that decides for itself: patrols, closes, swings, and can be blocked or parried on its 24-tick wind-up |
| 14 `Chaser` | never swings. Closes and holds station; its body is the weapon |
| 15 `Ambusher` | lies still until you are within 3.5 m, then commits to one lunge. It does not steer — step aside and it misses |
| 16 `Caster` | backs away to 6 m and throws bolts. Walking it down is the answer |

Stations 1–12 are scripted: dummies and `TrainingAttacker`s on timers, which is what they are for.
**Stations 13–16 are planner-driven** and behave differently every run — they are spaced well apart
because two of them inside each other's 12 m sight range spend their time answering questions about
each other rather than about you.

Stations 7–9 run on a 176-tick cycle (34 startup, 6 active, 26 recovery, 110 rest) with staggered
openings so they do not fire in unison. 34 ticks of wind-up is a little over half a second — long
enough to read, and the first number to change when the fight feels unfair.

The floor stripe at each station is the **connect band** — the range of foot positions from which
the standing slash reaches that target. Reach is one number; where you can stand is two, and the
difference is the part that is hard to feel.

**Verified in play mode**, not merely assumed: one press produced exactly one hit despite a
four-tick window; the standing slash left the squat dummy untouched while a same-tick crouch-plus-
attack landed `thrust_low` on it; and the cover dummy took nothing through the grate but took 10
from the same attack at the same distance on the near side.

The dodge was checked in the arena too, though less cleanly: it is enabled in the gray-box loadout,
cycles correctly, and steps the authored distance — but two attempts to measure that distance were
confounded by the character wedging against level geometry (station 6's ledge, then the east wall),
which took a while to recognise as geometry rather than a bug. The exact 2.2 m claim rests on the
headless test, which asserts it to 0.05 m.

The down-thrust was verified the same way: dropped onto the first pogo target with down held and
attack latched, it took **400 damage — twenty-five bounces at 16 each** — while the two targets
beside it took nothing. Each bounce re-armed the dive and re-hit the same target, which is the
dedup set resetting per action and the Zelda II pogo both working.

Defence was verified the same way. Standing still, the high telegraph landed 12 a swing on a
176-tick beat. With the guard up, the same attack became `Blocked` for 3 — and the guard survived
each one. Then, without changing anything but the station, the unblockable telegraph landed its
full 18 through that same raised guard. Same geometry, same reach, one authored flag different.

Regenerate with `Prophecy > Build > Generate GrayBox_Arena` after any change to `CombatTuning` —
the stations are sized from the moveset, so retuning reach without regenerating leaves the arena
asking the wrong questions.

---

## 10. Scale and multiplayer — decided 2026-07-30

**Co-op PvE only. No PvP, no competitive integrity requirement. Multiplayer lands in HopeFell
first.** That answer decides a lot, so it is written down rather than left to be re-derived.

### What it rules out, and why that is a relief

**No deterministic lockstep, and no rollback.** Both demand bit-exact determinism across peers *and*
cheap snapshot/restore of the whole sim at 60 Hz. Rollback is also in direct tension with the other
requirement here: 100 entities re-simulated eight frames deep is 800 entity-ticks inside one
rendered frame, every time a prediction misses. Wanting both 100+ enemies and 4-player rollback is a
scope decision, not an optimisation — and it is now moot.

So: **`CharacterSim` does not need to be snapshot-able**, and cross-platform bit-determinism is not
a requirement. Two of the three blockers from the original audit are gone.

### The model

**Host authority, client prediction, and the client authoritative over its own defensive state.**

The last part is the load-bearing bit. The parry window is **8 ticks — 133 ms**. At 60 ms one-way
latency the host would be judging a parry against state the client saw four ticks ago, out of a
window eight ticks wide, and it would feel broken. Letting the client assert "I was guarding on
tick N" and having the host apply it removes the problem entirely. It is trivially cheatable and
that does not matter in co-op.

The telegraph startup is 34 ticks — 567 ms — so the *read* has enormous slack. Only the window is
tight, and only the window needs this treatment.

The existing shape suits it well: `InputFrame` is already what you would send, the tick is fixed,
and `HitResult` coming back from the gate chain is already the right channel for "here is what I
made of that hit".

### Done — all seven, 2026-07-30

1. **Broadphase.** `HurtboxSet` buckets this tick's volumes on X and every query asks only the
   buckets it spans. X only on purpose: the play space is a lane a few metres tall and hundreds
   long, so bucketing Y would add arithmetic to separate things already within a body height of
   each other. A second axis is a change here and nowhere else.
2. **Contact is no longer O(n²).** Only sources that deal contact damage are considered, and each
   asks the broadphase rather than walking everyone.
3. **The contact key is packed into a `long`**, the way `HitSweep` already did. The old
   `source * 1000 + victim` gave 3000 for both (1, 2000) and (2, 1000), and the collision presented
   as a hit that silently never happened. There is a test for exactly that pair.
4. **Hit routing is a dictionary.**
5. **Ids are authored.** `Combatant._combatId` is serialized; the arena generator hands them out in
   build order from 10, the player prefab is 1, and anything spawned takes one from
   `CombatState.AllocateId()` above `FirstRuntimeId` so runtime and authored ranges cannot meet.
   Registering a duplicate is refused with an error rather than accepted — a duplicate does not
   fail loudly on its own, it silently routes someone else's hits to the wrong body.
6. **One `Update` for all combatants**, driven by the director, and the tint property block is only
   written when the colour actually changes.
7. **The fight moved into `CombatState`** — plain C#, holding the registry, the projectiles, the
   contact cooldowns and the hit routing. `CombatDirector` is now a MonoBehaviour that finds the
   scene's geometry, ticks on the clock, and drives the visual half. The authoritative state a host
   would replicate is no longer in presentation, and it is testable with no scene.

### And the geometry is ours

`ImmediatePhysics` is gone. `HitResolver` does separating-axis on two rotated rectangles — four
projections — with `DeterministicMath` for the axes and an axis-aligned fast path for the common
unrotated pair. No `Allocator.Temp` arrays, no native interop, runs everywhere Unity does, and
bit-identical across same-architecture targets. The console-porting entry has been removed from the
release checklist rather than deferred, and `ImmediatePhysicsProbeTests` deleted with it.

**Unity raycasts and trigger colliders remain the wrong direction** and were considered and
rejected: still PhysX so no determinism gained, they need a live `PhysicsScene` so the headless
contract and `SimArchitectureGateTests` both break, and they resolve on the physics timestep rather
than the fixed tick — the mistake HopeFell already paid for once with animation events.

### The scans that survived the first pass, 2026-07-30

"Every damage path goes through the broadphase" was true and still left four full walks of the
roster. Audited on challenge, and worth recording because three of the four were in the parts
nobody thinks of as the hot path:

- **`TrainingAttacker.FaceNearest`** compared against every hurtbox in the level to answer "which
  way do I turn", once per swing per attacker. Now a bounded `Query` over `_targetSearchRadius`
  (16 m) — nothing further away could have won.
- **`CombatState.ResolveContact`** swept the whole roster each tick looking for the two or three
  bodies that deal contact damage. Those are now picked out during `RebuildHurtboxes`, which is the
  one pass over everyone that cannot be removed, along with the index of the box each one published.
  Contact therefore also resolves against the tick's snapshot rather than a second `BuildHurtbox`.
- **`CombatDebugOverlay`** drew a cover ray to every hurtbox, per live box, per frame. Bounded to
  the swing's reach — which fixed the picture as much as the cost, since past a dozen bodies the fan
  of lines obscured the one ray it was meant to explain.
- **`HitResolver.Resolve(…, IReadOnlyList<Hurtbox>, …)`** had no runtime callers, but it was public
  and shared a name with the fast one. Renamed `ResolveWithoutBroadphase` and made `internal`
  (`Runtime/AssemblyInfo.cs` grants the test assembly access). The guard rail is the name: an
  overload set where the O(n) member is one autocomplete entry away from the right one is a
  regression waiting for whoever writes the next call site.

Pinned by two tests. `ATickAsksEachBodyWhereItIsExactlyOnce` counts `BuildHurtbox` calls per tick —
anything above one is a second walk of the roster. `ContactTestsTheSnapshotEveryoneElseResolvedAgainst`
uses a body that answers differently the second time it is asked, so rebuilding is a wrong answer
rather than a slow one.

`HurtboxSet.Query` is **not re-entrant** — one dedup stamp per set. Three callers share it now;
each reads its results into its own list before doing anything that could query again.

### What is still outstanding

Nothing from the list above. What remains is netcode proper — client prediction, replication, and
the promotion of combat out of `com.rokkan.prophecy` into a shared package — and none of it can be
built until HopeFell needs it.

### Sequencing

All seven are done, so the sequencing question they raised is settled: the two that got more
expensive with every module added — authored ids and getting the fight out of presentation — were
the reason to do this now rather than at promotion time, and they are the two that would have been
worst to retrofit.

What is left is genuinely deferrable. Client prediction and replication cannot be designed against
a game with no enemies in it, and the promotion of combat into a shared package should happen when
HopeFell needs it, not in anticipation. Doing either now would be guessing at requirements that
enemies are about to supply.

The one thing worth holding onto: **`CombatState` is the replication boundary.** It is plain C#, it
holds the registry, the projectiles, the contact cooldowns and the hit routing, and it is ticked by
one call. When a host has to serialise a fight, that object is the answer — keep it that way, and
resist putting anything into `CombatDirector` that a headless host would need.

---

## 11. Open design questions (raised, still unanswered)

None block M4; all cost money if discovered late.

1. **§5.7 contradicts §7** — Mirefen's sluices and Cordwell's horde arena are two whole subsystems
   in boss costumes.
2. **The "lose" ending is a fail state players will reload past.**
3. **Power-gating on total power is farmable**, so "your power *is* your complicity" quietly breaks.
4. **The dawning arrives too late to be a choice** — suggested fix: make binding revisitable.
5. **Delay vs. witnessing pull opposite ways** — at least one forced return must be authored.
6. **Aldhearth has no Protector** yet the win is "complete the binding". What is the final essence?
7. **The Seven Tenets may dilute the prophecy** — two reveals of identical shape blunt each other.
8. **The health economy and finishers must be decided together.** Kill-to-heal only pulls if health
   is otherwise scarce; if potions or Flame-Art healing exist, finishers stop driving anything.
9. **Can a parry cancel your own attack's recovery?** Three places disagree and one of them is the
   code. `AttackModule`'s doc says the arbiter gives "parry-cancels-recovery for free";
   `LockPriority.Reaction`'s doc says "Dodge/parry — may cancel an attack"; but `Block` locks at
   `Defend` (20) and a takeover needs strictly more than the attack's 30, so it never can. The
   dodge does, because `DodgeStep` kept `Reaction` (40). This was not a decision — parry had its
   own module at `Reaction` until it was folded into `Block` for the one-button merge (`5b8355c`)
   and inherited the guard's priority.

   **Deferred deliberately, 2026-07-30**, because it decides how committal an attack is and that is
   not judgeable against a dummy on a timer. The fix, when it is made, has to split the press from
   the hold — locking at `Reaction` for the whole guard would mean you could never start a swing
   out of a raised one, which is worse than the bug. Whatever is chosen, **make all three agree**.

10. **Two enemies can connect on the same tick, and the second hit's stun wins.**
    `SimClock` ticks characters in registration order, and a hit calls `CharacterSim.ReceiveHit`
    synchronously inside the attacker's slot. So on tick N both attackers resolve before the
    victim's `HitReact.Tick` runs — and that is where `HitInvulnerabilityTicks` is armed. Two
    consequences, verified in code 2026-08-03:

    - **Both hits deal full damage.** The i-frame gate reads `_invulnerableUntilTick`, which is
      still unset when the second sweep resolves; `Vitals.ApplyDamage` has no same-tick dedup.
      Post-hit invulnerability protects across later ticks, never within one.
    - **Only one stun survives.** `_pendingStun` is a single slot and `Stun()` overwrites it, so
      the shove comes from whichever attacker registered later.

    Damage is therefore order-independent, but **knockback direction is not** — which matters for
    host-authoritative netcode if spawn order ever differs between machines, though not for the
    30/60/144 fps guarantee (frame rate does not change tick order).

    **Deferred deliberately, 2026-08-03** — not judgeable until the orchestrator decides how many
    enemies may attack at once, because that is the knob that makes same-tick doubles common.
    Same-tick double damage is defensible ("two enemies genuinely both hit you"); silently dropping
    one hit's shove is the shakier half. Options are first-wins, strongest-wins, or combine. Decide
    it **with** the orchestrator, not before.

11. **The combat orchestrator — scope, and how it survives 2D → 3D.**
    Raised 2026-08-03. Intelligent enemies need arbitration so a group does not all swing at once.
    The seam is clean: a sim-side system grants attack tokens, a GOAP sensor publishes
    `HasAttackToken`, and `Swing` takes it as a precondition — refused enemies then fall back to a
    lower-priority goal on their own, because the planner already does that.

    Two constraints came out of the discussion and both narrow the design:

    - **Tokens are space-agnostic; positional slots are not.** A ring of standing positions assumes
      the top-down overworld, while the side-scroll lanes only have "left of you" and "right of
      you". So the portable core is the token; any spatial policy has to be per-space and pluggable,
      or it gets designed twice.
    - **A single global cap is wrong once ranged exists.** A bolt from 12 m and a swing at 1.4 m are
      different kinds of pressure and should not draw on the same budget.

    Not started. Note that a token system's entire purpose is coordinating several enemies at once,
    which is precisely the condition the shared-state trap in `CLAUDE.md` needed — its state must
    live in one sim-side system the enemies *read*, never in anything each of them holds a piece of.
