# Gray Box Build Plan

> Implementation plan for the Proxy & Gray Box Phase (design bible section). Goal: a playable gray box running the full final-state moveset against gray geometry, ending in a gray Protector fight. Follows the lock-order: proportions → skeleton → moveset architecture → animation timings → feel tuning. Art stays out until exit criteria pass.

---

## Step 0 — The one decision that blocks everything: proportions

Per the doc, final Light Bearer proportions (stylized vs. realistic anime) must be chosen **before** the proxy is generated, because proportions drive reach, silhouette, hitboxes, and jump readability — and switching after tuning invalidates the tuning.

**Action:** decide proportions first. A cheap way to decide: generate 2–3 throwaway Meshy characters at different proportions (chibi-ish stylized, mid anime, realistic anime), drop them in an empty Unity scene at 1 unit ≈ 1 m next to gray cubes, and pick by silhouette at gameplay camera distance. These test meshes cost minutes and are discarded.

## Step 1 — Unity project scaffold

1. **Unity 6** (Meshy's Unity plugin is tested against 6000.0.45f1), **URP** template. Cel-shading comes later; URP from day one so nothing has to migrate.
2. Scene structure per the design doc's architecture: a persistent **Bootstrap** scene (owns cross-scene state later; near-empty now) + **GrayBox_Traversal** and **GrayBox_Arena** side-scroll scenes loaded over it.
3. **Input System** package, one action map covering the whole final-state moveset (including flags-off abilities).
4. Install the **Meshy Unity plugin** (.unitypackage from meshy.ai/integrations/unity) for one-click Bridge import of generated models.
5. Stub `PlayerState` now only as far as the gray box needs (none of it gates this phase — all ability flags ship on).

Deliverable: empty project that enters play mode with a capsule sliding on gray cubes. Nothing else.

## Step 2 — Proxy hero (Meshy pipeline)

The mesh is disposable; the skeleton and animation set are what get locked.

1. **Generate** the proxy in Meshy (Text to 3D, or Image to 3D from a proportion-reference sketch). Prompt for the chosen proportions, **T-pose or A-pose** — Meshy's auto-rig depends on it. Quality doesn't matter; shape does.
2. **Remesh** in Meshy before rigging (their docs: clean topology → accurate bone binding; low-poly is fine and preferable).
3. **Auto-rig** in Meshy (humanoid skeleton, Mixamo-compatible). Preview weights, test the T/A-pose toggle.
4. **Export FBX** (with rig), import to Unity — via the Bridge or manually.
5. In Unity import settings: **Rig → Humanoid**, generate the avatar, verify the avatar mapping is green across the board. This is the load-bearing step — the Humanoid avatar is what makes every animation tuned here retarget to the final model automatically.
6. Validate retargeting immediately: drop one Meshy preset clip and one Mixamo clip on the avatar. If both play clean, the skeleton is lockable.

Same pipeline, second pass: the **gray Protector** — one oversized hulk, own Humanoid avatar, shared by all seven Protectors later. Can wait until Step 6 so it doesn't block hero work.

## Step 3 — Modular controller architecture

Build the skeleton of the code before any feel work, exactly as specced:

- One `CharacterController2_5D` (kinematic Rigidbody, axis-constrained) that owns state (grounded, stance: stand/crouch/air, facing) and a module registry.
- Each ability = one module (component) with an `Enabled` flag: Walk, Run, Crouch, Crawl, Jump, DoubleJump, DownThrust, Land, LedgeHang, LedgePullUp, LadderClimb, StanceAttack, Parry, Block, DodgeStep, HitReact, FlameArt.
- Modules talk only through the controller — never to each other.
- Animator: base locomotion blend tree (idle→walk→run), combat/traversal as layers/overrides per module.
- All flags **on**. Progression later is just a schedule of flag flips.

Deliverable: controller running with Idle/Walk/Run/Jump/Crouch modules on placeholder animations. Add modules incrementally; each new module must not touch existing ones (that's the architecture test).

## Step 4 — Animation set

- **Generic actions** (idle, walk, run, jump, land, climb, hit-react): pull from Meshy's preset library (500+ clips; walk/run, fighting categories) and Mixamo. Meshy exports clips on the rigged FBX; multiple clips per export.
- **Signature moves** (down-thrust, parry, stances, dodge-step): preset libraries won't have these as you need them. Expect to source nearest-fit fighting clips and iterate — retime/blend in Unity, or export the rigged FBX to Blender for keyframe edits (Meshy's own recommended path for custom motion). These get disproportionate iteration; they are the Zelda II identity.
- Once combat is tuned against windups/active frames/recovery/parry windows, timings are **locked** (lock-order #4). Track them in a single data asset so they're numbers, not tribal knowledge.

## Step 5 — Gray box environment + feel tuning

Two scenes, primitive/ProBuilder geometry only, all dimensions derived from the tuned movement numbers:

- **GrayBox_Traversal:** flat runs, jump gaps sized off run speed, single/double-jump heights, ledges at hang height, crawl spaces at crawl height, ladders with mount/dismount at both ends, moving-through-combat corridors. As numbers tune, this scene becomes the living dictionary of standard level dimensions the Ashmoor slice will be built from.
- **GrayBox_Arena:** a flat Protector arena with a couple of jumpable/parryable telegraph dummies.

Feel-tuning priorities per the doc: jump is code, not animation — gravity curves, apex hang, coyote time, jump buffering first; ledge hang detection/hand-snap second (the fiddliest, budget real iteration); then hit-stop, knockback, i-frames on the combat kit.

## Step 6 — The gray Protector fight

- Proxy hulk from Step 2's second pass, on the shared combat skeleton.
- Build the Ashmoor/Wrath moveset as the template — it's the tutorial cap and needs no tool: overhead slam (jump it), lesser hits (block), low gore (crouch), telegraphed ember-burst (parry to stagger open). Damage via normal strikes + down-thrust.
- Tune the **weight of the kill** with zero art: music drop, lights guttering, region-darkening stub. All seven Protectors inherit whatever this proves.

## Step 7 — Exit criteria (from the doc, unchanged)

1. Traversing the empty test scene is fun for its own sake (the Mario test).
2. A full gray Protector fight lands — readable, fair, heavy — with zero art.
3. The Ashmoor slice's level design can be built from the tuned movement numbers without reopening them.

Only then does the art pipeline run. Proxy meshes are discarded; skeletons, animations, and tuning survive untouched.

---

## Build order at a glance

| # | Milestone | Blocks on | Meshy involved |
|---|---|---|---|
| 0 | Proportions decision | — | throwaway comparison meshes |
| 1 | Project scaffold | — | plugin install |
| 2 | Proxy hero rigged + retarget-verified | 0, 1 | generate → remesh → rig → FBX |
| 3 | Modular controller (locomotion core) | 1 | — |
| 4 | Full moveset + animation set | 2, 3 | preset clips; Blender round-trip for signatures |
| 5 | Gray box scenes + feel tuning | 3 (deepens with 4) | — |
| 6 | Gray Protector fight | 2 (2nd pass), 4, 5 | proxy hulk pipeline |
| 7 | Exit criteria → art phase | all | — |

## Meshy notes worth knowing (from their docs)

- Rig pipeline order matters: **Remesh → Rig → Animate**; T/A-pose input; humanoid rigs are Mixamo-compatible → clean Unity Humanoid mapping.
- Export **FBX** for Unity (textures separate — irrelevant for gray box). Auto-rig can't do facial/tail bones; not needed this phase.
- Unity plugin tested on Unity 6 (6000.0.45f1); Bridge = one-click send from webapp to scene.
- Abnormal proportions degrade auto-rig quality ("close to standard human body") — relevant for the Protector hulk; if its rig comes out bad, rig manually in Blender or keep it closer to human-scale and scale the transform in Unity.

## Open knobs surfaced by this plan

1. **Context walk vs. analog blend** — flagged open in the doc; the gray box is where it gets answered. Build walk as a toggle first (digital Zelda II lean), test both.
2. **Protector scale vs. rig quality** — how oversized can the hulk be before Meshy's auto-rig degrades? Test early in Step 2's second pass.
3. **Where signature-move animation iteration happens** — Unity retiming vs. Blender keyframing. Decide after the first down-thrust attempt; whichever it is becomes the standing pipeline for all seven Protector signatures.
