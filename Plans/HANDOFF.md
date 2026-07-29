# Prophecy — session handoff

**Written:** 2026-07-29, end of the session that built M3 and started M4's foundations.
**Resume at:** M4 — `HitWindow` + `AttackTimeline`, then stance attacks. The collision half is done.

This is the "where we are and why" document. Standing rules live in `CLAUDE.md` at the repo root
(loaded automatically) — this file does not repeat them. Design canon is `Plans/Design-Bible.md`;
the phase plan is `Plans/Gray-Box-Build-Plan.md`; dev-only settings that must be reverted before
shipping are in `Plans/Release-Checklist.md`.

---

## 1. Current state

| | |
|---|---|
| Branch | `baseline/unity-project-and-design-docs` (**still not merged to `main`**) |
| HEAD | `ec725d5` |
| Working tree | clean except untracked `ProjectSettings/ProjectAuditorSettings.asset` |
| Tests | **195 passing, 0 failed, 0 skipped** (~2.8 s) |
| Unity | 6000.5.0f1, URP active, Input System only, **Cinemachine 3.1.7** |

To put the work on `main`: `git checkout main && git merge --ff-only baseline/unity-project-and-design-docs`

### Commits this session

```
ec725d5  M4 foundations: validate ImmediatePhysics, add cover queries
e2a0ade  Refresh the handoff for the end of M3
3583238  Context-sensitive camera offsets, and restore the dead zone
bf4ec8f  Adopt Cinemachine; frame the camera in level lanes
3df4d0b  Drop-through platforms, anchored ladders and ropes, level camera
e8e47ca  Enable runInBackground for editor-driven play testing
0053c4b  Add pre-release checklist for dev-only settings
0e3b461  M3: presentation layer, scenes, and the traversal moveset
67ec3ad  M3 (sim half): movement tuning, ability modules, headless tests
```

### Three repos are in play

| Repo | Path | State |
|---|---|---|
| **Prophecy** | `RGS\Prophecy\Prophecy` (Unity project nested one deeper) | active, above |
| **Shared packages** | `RGS\Packages` | own git repo, HEAD `413862e`, clean — **untouched this session** |
| **HopeFell** | `RGS\HopeFell` | ⚠️ **still untouched, and must stay that way for now** |

Nothing in `RGS\Packages` changed during M3, so `MIGRATION-HopeFell.md` needs no new entries. M4
may change that — the `AttackTimeline` pattern comes from HopeFell.

---

## 2. What exists

### Prophecy — `Packages/com.rokkan.prophecy/`

```
Runtime/Sim/
  MovementTypes.cs        MovementSpace, Stance, LockFlags, RunMode, ClimbableKind, AttachmentKind
  InputFrame.cs           ButtonState (Held/Pressed/Released), InputFrame — whole final moveset
  CharacterState.cs       position(FEET)/velocity/grounded/stance/facing, Attachment, DropThrough
  ActionLock.cs           ActionLock struct + LockPriority constants
  AbilityModule.cs        abstract base: Enabled, Id, Order, ValidIn, Tick, Reset
  AbilityId.cs            stable per-ability identity — what a save file stores
  AbilityLoadoutData.cs   which abilities are on; absent entry means "leave alone", never "off"
  MovementTuningData.cs   every number, incl. LaneHeight (= StandHeight x 2)
  CharacterSim.cs         ISimSystem: registry + lock arbiter + movement resolution
  PlayerCharacterFactory  builds the character tests and the game both use
  Collision/Aabb.cs           value type, strict overlap
  Collision/CollisionWorld.cs solids + one-way/pass-through + climbables, sweeps, wall & ledge probes

Runtime/Sim/Abilities/   Gravity, GroundMove, TopDownMove, Crouch, DropThroughPlatform, FallLand,
                         Jump, DoubleJump, WallJump, WallSlide, LedgeHang, LedgePullUp,
                         LadderClimb, DownThrust, Interact  (built)
                         Crawl, DodgeStep, FlameArt         (declared, ship disabled)
Runtime/Core/            MovementTuning, AbilityLoadout  (ScriptableObject shells)
Runtime/Presentation/    ButtonLatch, PlayerInputCapture, PlayerCharacterHost, CharacterView,
                         CollisionBaker, SpaceMapping, LaneCameraRig, MovementDebugOverlay,
                         OneWayPlatform, LadderVolume
Runtime/World/           SceneDescriptor, SpawnPoint, SceneDirector, BootstrapLoader
Editor/Build/            ProphecyAssetBootstrap, InputAssetGenerator, GrayBoxTraversalBuilder,
                         BuildSettings
Tests/Editor/            CharacterSimTests 23, CollisionWorldTests 22, SimArchitectureGateTests 6,
                         PackageWiringTests 4, MovementTests 32, InputLatchTests 7,
                         TraversalAbilityTests 30+   → 177 total
```

### Prophecy — `Assets/_Prophecy/`

```
Data/MovementTuning.asset          the live tuning surface
Data/AbilityLoadout_GrayBox.asset  17 rows, everything on except the 3 unbuilt
Input/Prophecy.inputactions        generated; Gameplay + Debug maps, 10 actions, 30 bindings
Prefabs/Player.prefab              host + capture + view + capsule body
Scenes/Bootstrap.unity             persistent: SimClock, Player, Main Camera (+Brain),
                                   CM_Gameplay (+LaneCameraRig), CameraTarget, UI, SceneDirector
Scenes/GrayBox_Traversal.unity     generated; 26 solids, 2 climbables
```

**Not yet built:** no combat of any kind. No enemies, no hitboxes, no health, no `AttackTimeline`.
No overworld scene (`TopDownMove` exists but top-down motion is not swept against the world — see
gaps). No animation system. `Assets/MeshyImports/` is still throwaway test generations.

---

## 3. Decisions already made — do not re-litigate

Carried from the previous handoff and still true:

1. **Feel first**, capsule proxy now, placeholder overworld in scope, full sim/presentation
   contract, shared packages in a neutral home, and taking `com.rgs.core` / `AttackTimeline` /
   GOAP-if-it-earns-it / persistence from HopeFell.
2. **Sim owns its own `CollisionWorld`.** Unity physics can't run headless.
3. **Combat timing in ticks, never animation events.** HopeFell already paid for this.
4. **One action lock, not a stack.** Takeover needs higher priority *and* an open cancel window.
5. **Position is the feet.** **Overlap is strict.** **Coyote counts ticks, not seconds.**

Added this session:

6. **Three scenes, not four.** Bootstrap persists; world scenes swap under it; UI splits out when
   it earns it. The player is a **prefab in Bootstrap, not its own scene** — Unity does not
   serialise cross-scene references, so a Player scene would turn every host/view/camera link into
   runtime resolution. `SceneDescriptor` carries per-scene config so the player can persist while
   the *configuration* varies.
7. **Progression is an `AbilityLoadout` asset.** Every ability is always registered; which ones
   tick is data. An absent row means "leave alone", never "off" — otherwise every ability added
   after an asset was authored would silently vanish.
8. **Levels are composed in lanes.** A lane is the floor-to-floor module, `StandHeight x 2` =
   3.6 m, derived so the grid follows if the hero is resized. The camera frames 4 lanes, puts the
   player in the centre lane when free, and clamps to level bounds — at which point the player
   rides up the frame, which also signals there is nothing below.
9. **Cinemachine, Follow only, no Aim.** Rotation Composer and Hard Look At tilt the camera to
   track the target, which makes vertical surfaces converge and the axis-constrained plane stop
   reading as flat.
10. **Camera framing is settled; the feel numbers inside it are not.** See §6.

Added while starting M4:

11. **Hitboxes are sim-side, not trigger colliders.** Design bible §6.1 and §7 both say
    "trigger-collider hitboxes"; **superseded**, and recorded as such in `CLAUDE.md`. Trigger
    callbacks fire on the physics timestep rather than the 60 Hz tick, which is the determinism
    killer — and it is independent of shape.
12. **Hit geometry uses `UnityEngine.LowLevelPhysics.ImmediatePhysics`, not hand-rolled AABBs.**
    The deciding factor is rotation: an axis-aligned box cannot express a sword arc or a sweeping
    tail. It was validated first (see §5a) rather than assumed.
13. **Some hits are stopped by level geometry, decided per attack.** A spear thrust through a grate
    stops; a shockwave does not. The world only answers the geometric question
    (`CollisionWorld.IsOccluded`); whether an attack cares is attack data.
14. **One definition of solid, two questions.** `ImmediatePhysics` resolves hitbox against
    hurtbox; `CollisionWorld` resolves cover against the same baked geometry movement uses. There
    is deliberately no second copy of the level in the combat system.

---

## 4. Traps already hit — do not rediscover

Carried forward, still true: **both `.gitignore` files are required**; **tests in `file:` packages
need `testables`**; **`-batchmode -runTests` refuses a project an Editor has open**; **two
`SimClock` bugs were fixed during the port**; **`[MovedFrom]` does not protect scene references, so
do not regenerate the shared `Sim` `.meta` files**; **two red `[RandomSource]` lines during test
runs are HopeFell's own fallback test, not a failure.**

New this session:

7. **Play mode freezes when the Editor loses focus.** `runInBackground` was off, so any
   tooling-driven play test stalled at `frameCount = 1` — which looks exactly like broken gameplay.
   Now on (commit `e8e47ca`), and listed in `Plans/Release-Checklist.md` to be reverted before
   shipping.
8. **A failed `Unity_RunCommand` exits play mode.** The compile error triggers an assembly reload.
   Diagnostics taken afterwards are edit-mode readings where `Update` never ran — this cost real
   time and nearly produced a wrong conclusion. **Always check `Application.isPlaying` before
   trusting a play-mode measurement.**
9. **Cinemachine ≤ 3.1.4 does not compile on Unity 6000.5** — it calls `Object.GetInstanceID()`,
   now an error-level obsolete (CS0619). 3.1.4 is what sits in the local package cache, so an
   offline install picks the broken one. Use **3.1.7**.
10. **Asset references can silently fail to persist** when set on a prefab moments after the assets
    were imported. They come back null with no error. Verify by re-reading through a *fresh*
    `SerializedObject`, and prefer `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset`.
11. **A prefab cannot reference a scene object** — `PlayerCharacterHost._clockDriver` is null in
    the prefab by design. `SimClockDriver.RegisterWithScene` has a scene-search fallback for this.
12. **`EditorSceneManager.NewScene(..., Additive)` throws while an untitled scene is open**, which
    is the state a freshly launched Editor is always in. Generators use `Single` and restore the
    previous setup.
13. **Camera bounds and camera offsets write the same number.** Measuring one requires neutralising
    the other, or the clamp silently dominates the reading.
14. **`ImmediatePhysics.GenerateContacts` rejects `contactDistance` of zero** — "must be positive
    and not equal to zero". "No inflation" has to be an epsilon (`0.0001f`). Read the other way it
    is a free forgiveness knob: a generous hitbox without resizing geometry.
15. **Rotation makes intuition about box overlap unreliable.** A 0.5 half-extent box projects 0.5
    along X unrotated but 0.707 turned 45°. Two boxes 1.32 m apart therefore *miss* axis-aligned
    and *connect* rotated. Worth deriving rather than eyeballing when authoring hit volumes.

---

## 5. Tooling

**Unity's own MCP (`mcp__unity-mcp__*`)** is registered and connected. Use it for scenes, prefabs,
play mode and console reads. `Unity_RunCommand` compiles C# in a dynamic assembly that **cannot
reference project assemblies** — resolve project types by reflection over `AppDomain`, and note it
rejects `System.Reflection` as a namespace import (call `.GetType().GetProperty(...)` via `var`
instead). Its logger does **not** honour format specifiers — pre-format with `string.Format`.

**Generators** (`Prophecy > Build > …`): `Create Missing Data Assets`, `Generate Input Actions`,
`Generate GrayBox_Traversal`. All idempotent, all also exposed as `-executeMethod` targets.
**Tests**: `Prophecy > Tests > Run EditMode Tests` writes `Logs/test-results.txt`.

The **CLI is `Unity.exe` itself** (no `unity` on PATH) and refuses a project the Editor has open.

---

## 6. Movement and camera numbers are provisional

The user played the gray box, judged it **"good enough to move on"**, and said explicitly it will
need better tuning **once models and combat exist**. Every value was derived or reasoned to, none
were felt against real art.

Most suspect: `_lookAheadLanesAtRun` (1 lane), `_verticalDeadZoneLanes` (0.8), `_fallLookDelay`
(0.33 s), `LedgeGrabMinHeight`/`MaxHeight` (1.0/1.7 m, pure apex maths), `AirJumpArmTicks` (8).

Camera *structure* — 4 lanes visible, centre-lane framing, half-lane offsets, bounds clamping — is
settled. It is the feel numbers inside that structure that are open. Do not build level geometry or
combat timing that depends on an exact current value surviving; derive from tuning as the gray box
generator does.

---

## 7. Known gaps carried into M4

1. **Top-down has no collision.** `CharacterSim.Integrate` skips the sweep in `MovementSpace.TopDown`,
   so an overworld player walks through walls. Must be fixed before the overworld scene means anything.
2. **No overworld scene exists yet** — `Overworld_GrayBox`, portals and `SceneDirector.GoTo` are
   written but only exercised by a single world scene.
3. **`DownThrust.Bounce()` is never called.** The dive, the commitment lock and the bounce exist;
   the hitbox that would trigger it is M4's job.
4. **`Interact` produces a request and a probe box that nothing consumes.**
5. **F1 overlay ships visible**, gray-box loadout has everything on — both in the release checklist.
6. **`ImmediatePhysics` is validated on Windows only.** No console playback engines are installed,
   so the project cannot build for Switch 2 / Xbox / PS5 at all today. It is a built-in engine
   module over per-platform native PhysX, so it should ship everywhere — but that is reasoning, not
   evidence. In the release checklist.
7. **Cross-platform determinism of hit resolution is unproven and probably false.** PhysX contact
   generation is floating point and Switch 2 is ARM against x86-64 elsewhere. Harmless for
   single-player; fatal for replays, ghost data or lockstep netcode. Movement never touches PhysX,
   so the 30/60/144 guarantee is unaffected.

---

## 8. M4, combat — done so far, and what is next

Goal: **stance combat that feels like Zelda II, timed in ticks, testable headless.**

### Already built (commit `ec725d5`)

- **Hit geometry backend chosen and validated.** `ImmediatePhysics` runs with no scene, no
  collider and no `PhysicsScene`. `ImmediatePhysicsProbeTests` (7) pins overlap, separation,
  rotation, the contact-distance skin, `Normal`/`Separation` payload, batched pairs and
  repeatability. **Compiles with no asmdef changes**; the geometry types are structs so the gate is
  unaffected.
- **Cover queries.** `CollisionWorld.IsOccluded(from, to)`, length-bounded slab test, 11 tests.

### The target: a full window decomposition, all in ticks

**Prophecy's combat goal is startup / active / recovery phases plus hit, parry, i-frame and cancel
windows — every one of them authored in ticks.** That is the requirement, confirmed 2026-07-29.
Fighting-game frame data, not a single on/off hit flag.

This matters because it is *more* than HopeFell has, and the difference is the whole reason M4 is
real work rather than a port. It is also what the Ashmoor fight needs: reading a telegraph means
the startup phase is authored, visible and long enough to answer, and the four answers (jump,
block, crouch, parry) each depend on a different window being open at the right tick.

### What HopeFell actually has — do not plan against more than this

`HopeFell/Packages/com.rokkan.gameplay/Runtime/Combat/Sim/` contains far less than the target
above. Read this before assuming anything is inheritable:

- `AttackTimeline : ISimSystem` + `HitWindow` — **one window only**: hit open/close, two ints in
  absolute ticks, `-1/-1` meaning "no hit". Startup is implicit (ticks before open), recovery is
  implicit (keeps counting until `Disarm`). No decomposition.
- **Hitbox *geometry* is still Unity trigger colliders** riding the animated weapon. Only *timing*
  moved to the sim. Their own slice report is candid this was scoped. So HopeFell is a template for
  the timing half only — we are going further.
- **Parry and i-frames never reached the tick.** `BlockProfileSO.ParryWindow` is `0.2f` *seconds*;
  `DodgeTuningSO.invulnStart/End` are seconds accumulated in `Update()` with `Time.deltaTime`.
  Porting as-is would import a frame-rate-dependent parry window.
- **Cancel windows are normalized clip time**, deliberately and with an argument — which
  contradicts our `CLAUDE.md` rule. Keep ours; we have no clips anyway.
- **No action lock or arbiter at all** — a `Phase` enum plus `IsBusy` on a 1,324-line
  `SkillExecutor`. **Our `ActionLock` (priority + cancel window) is the better design.** Keep it.

Worth taking: `HitWindow`'s two-int tick model and its rationale; the `IHitResolver` indirection
that keeps the timeline headless; `IDamageGate`/`DamageGateResult`/`DamageContext` as a first-wins
gate chain; and their "relocate, don't re-feel" discipline.

### Next

1. **`AttackTimeline` with the full decomposition** — `StartupTicks` / `ActiveTicks` /
   `RecoveryTicks` as a partition (so phases cannot overlap or leave gaps), plus absolute-tick
   ranges for hit windows (plural — multi-hit), i-frames, parry and cancel. Arm/tick/resolve behind
   a resolver seam so the geometry backend stays swappable. If it lands in `RGS\Packages`, log it
   in `MIGRATION-HopeFell.md`.
   - The **cancel window plugs straight into the existing arbiter**: `ActionLock` already models
     "higher priority may take over only while the cancel window is open", so the timeline drives
     `sim.SetCancelWindow(...)` and parry-cancels-recovery falls out for free.
   - **I-frames** become a damage gate; **parry** a defence-resolution window. Both in ticks —
     this is exactly where HopeFell stopped, so there is nothing to copy and every reason not to
     reach for seconds.
2. **Attack definition data** — per-attack hit volume (offset, half-extents, rotation, facing
   flip), all the tick windows above, and a `StoppedByGeometry` flag feeding `IsOccluded`.
   Lock-order #4 says these live in **one data asset** so they are numbers, not tribal knowledge.
3. **Stance attacks** — high standing, low crouching, and the down-thrust's damage half wired to
   the existing, currently-uncalled `DownThrust.Bounce()`. New attack modules must not require
   editing an existing module.
4. **Defence, all in ticks** — block by stance, parry window, i-frames, knockback, hit-react at
   `LockPriority.HitReact`. This is where HopeFell stopped; do not inherit its seconds.
5. **One telegraph dummy** demanding the four Ashmoor answers: **jump the overhead slam, block the
   lesser hits, crouch the low gore, parry the ember-burst.** Then `GrayBox_Arena`.
6. **Tests** — identical combat outcomes at 30/60/144, parry windows asserted in ticks, and the
   architecture test that a new attack needs no edit to an existing module.

`com.rokkan.animation` (clip injection) is pencilled in for M4. Remember the rule: **clip events
drive VFX/SFX/footsteps only** — gameplay decisions live on the timeline. HopeFell's
`ClipEventChannel` carries the tombstone: *"Do not re-attach gameplay decisions to these."*

---

## 9. Open design questions (raised, still unanswered)

Unchanged from the previous handoff; none block M4, all cost money if discovered late.

1. **§5.7 contradicts §7** — Mirefen's water-temple sluices and Cordwell's endless-horde arena are
   two whole subsystems in boss costumes.
2. **The "lose" ending is a fail state players will reload past.**
3. **Power-gating on total power is farmable**, so "your power *is* your complicity" quietly breaks.
4. **The dawning arrives too late to be a choice** — suggested fix: make binding revisitable.
5. **Delay vs. witnessing pull opposite ways** — at least one forced return must be authored.
6. **Aldhearth has no Protector** yet the win is "complete the binding". What is the final essence?
7. **The Seven Tenets may dilute the prophecy** — two reveals of identical shape blunt each other.
