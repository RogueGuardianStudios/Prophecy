# Prophecy — session handoff

**Written:** 2026-07-29, at the end of the session that built M3.
**Resume at:** M4 — combat. The `AttackTimeline`, stance attacks, and the down-thrust's damage half.

This is the "where we are and why" document. Standing rules live in `CLAUDE.md` at the repo root
(loaded automatically) — this file does not repeat them. Design canon is `Plans/Design-Bible.md`;
the phase plan is `Plans/Gray-Box-Build-Plan.md`; dev-only settings that must be reverted before
shipping are in `Plans/Release-Checklist.md`.

---

## 1. Current state

| | |
|---|---|
| Branch | `baseline/unity-project-and-design-docs` (**still not merged to `main`**) |
| HEAD | `3583238` |
| Working tree | clean except untracked `ProjectSettings/ProjectAuditorSettings.asset` |
| Tests | **177 passing, 0 failed, 0 skipped** (~2.2 s) |
| Unity | 6000.5.0f1, URP active, Input System only, **Cinemachine 3.1.7** |

To put the work on `main`: `git checkout main && git merge --ff-only baseline/unity-project-and-design-docs`

### Commits this session

```
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

---

## 8. Next up — M4, combat

Goal: **stance combat that feels like Zelda II, timed in ticks, testable headless.**

1. **`AttackTimeline`** — authored tick counts for startup / active / recovery, hit windows, parry
   windows, i-frames, cancel windows. Ported from HopeFell's pattern. Log the port in
   `RGS\Packages\MIGRATION-HopeFell.md` if it lands in a shared package.
2. **Stance attacks** — high standing, low crouching, and the down-thrust's damage half wired to
   the existing `DownThrust.Bounce()`. Attack modules go on the same registry and must not require
   editing an existing module.
3. **Hitboxes and hurtboxes** — sim-side AABBs, resolved in the tick, not Unity trigger colliders,
   or the headless contract breaks.
4. **Defence** — block by stance, parry with a tick window, i-frames, knockback, hit-react at
   `LockPriority.HitReact`.
5. **One enemy** with high/low guard, to force the stance choice §6.1 is built around.
6. **Tests** — identical combat outcomes at 30/60/144 fps, parry windows asserted in ticks, and the
   architecture test that a new attack needs no edit to an existing module.

`com.rokkan.animation` (clip injection) is pencilled in for M4. Remember the rule: **clip events
drive VFX/SFX/footsteps only** — gameplay decisions live on the timeline.

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
