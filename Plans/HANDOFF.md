# Prophecy — session handoff

**Written:** 2026-07-29, at the end of the session that set the project up.
**Resume at:** M3 — movement, tuning, presentation. The Mario test.

This is the "where we are and why" document. Standing rules live in `CLAUDE.md` at the repo root (loaded automatically) — this file does not repeat them. Design canon is `Plans/Design-Bible.md`; the phase plan is `Plans/Gray-Box-Build-Plan.md`.

---

## 1. Current state

| | |
|---|---|
| Branch | `baseline/unity-project-and-design-docs` (**not merged to `main`**) |
| HEAD | `67c806c` |
| Working tree | clean |
| Tests | **109 passing, 0 failed, 0 skipped** (~0.15 s) |
| Unity | 6000.5.0f1, URP active, Input System only |

To put the work on `main`: `git checkout main && git merge --ff-only baseline/unity-project-and-design-docs`

### Commits

```
67c806c  Switch to Unity's own MCP; drop Coplay entirely
25f96a4  M2: sim foundation -- CollisionWorld, CharacterSim, action locks, gate
d5bd5df  Add in-editor test runner menu; M1 verified green
998461e  Add testables array so shared-package tests actually run
561cab1  M1: consume shared packages, add com.rokkan.prophecy
8afcb18  Remove orphaned Coplay package residue
f626562  Baseline: Unity 6.5 URP project, design bible, and repo hygiene
baf63c0  Initial commit (pre-existing)
```

### Three repos are in play

| Repo | Path | State |
|---|---|---|
| **Prophecy** | `RGS\Prophecy\Prophecy` (Unity project nested one deeper) | active, above |
| **Shared packages** | `RGS\Packages` | own git repo, HEAD `413862e` |
| **HopeFell** | `RGS\HopeFell` | ⚠️ **untouched, and must stay that way for now** |

HopeFell is mid-work: branch `foundry/docs-tdd`, 15 uncommitted changes (deleted Stalberg ScriptableObjects, an untracked `Assets/Stalberg/`, a `_Recovery` scene, four work-order docs). That is why the shared packages were **copied, not moved**. Do not modify HopeFell until that work is committed and its owner is ready. `RGS\Packages\MIGRATION-HopeFell.md` is its adoption checklist.

---

## 2. What exists

### Shared — `RGS\Packages\`
- **`com.rgs.core` 0.2.0** — foundation (`SerializableGuid`, `TagMask128`, RNG streams, `ISerializer`/`JsonSerializer`, `PersistentSingleton`, `VectorMath`, `DeterministicMath`) **plus `RGS.Core.Sim`**: `SimClock`, `ISimSystem`, `SimTickInfo`, `SimConstants` (60 Hz), `SimClockDriver`.
- **`com.rokkan.core`** — `SavePrimitives.cs` (`ISaveable`, `IBind<T>`).

Consumed by Prophecy via `file:../../../../Packages/<name>` — four levels up, relative to the **Packages folder**.

### Prophecy — `Packages/com.rokkan.prophecy/`
```
Runtime/Sim/
  MovementTypes.cs     MovementSpace, Stance, LockFlags
  InputFrame.cs        ButtonState (Held/Pressed/Released), InputFrame
  CharacterState.cs    position(FEET)/velocity/grounded/stance/facing, LastGroundedTick
  ActionLock.cs        ActionLock struct + LockPriority constants
  AbilityModule.cs     abstract base: Enabled, Order, ValidIn, Tick, Reset
  CharacterSim.cs      ISimSystem: registry + lock arbiter + movement resolution
  Collision/Aabb.cs           value type, strict overlap
  Collision/CollisionWorld.cs AABBs + one-way platforms, axis sweeps, IsGrounded
Editor/
  TestRunnerMenu.cs    Prophecy > Tests > Run EditMode Tests
Tests/Editor/
  CharacterSimTests.cs         23
  CollisionWorldTests.cs       22
  SimArchitectureGateTests.cs   6
  PackageWiringTests.cs         4
```

**Not yet built:** `Assets/_Prophecy/` does not exist. No scenes beyond the stock `SampleScene`. No prefabs, no `MovementTuning`, no movement modules, no presentation layer. `Rokkan.Prophecy.dll` compiles but nothing instantiates a `CharacterSim` at runtime yet.

---

## 3. Decisions already made — do not re-litigate

Agreed with the user during planning:

1. **Feel first.** M3 is the "is moving around fun for its own sake" test. Binding/region-darkening comes after the numbers are locked.
2. **Capsule proxy now.** Code track does not wait on the Meshy art track. Humanoid avatars make the later swap free.
3. **Placeholder overworld is in scope** — a gray top-down scene entered from side-scroll exits and left via portals, both directions, real scenes.
4. **Full sim/presentation contract**, same as HopeFell — plain C# on a fixed tick, headless, gate-tested.
5. **Shared packages, one source of truth**, in a neutral home neither project owns.
6. **Also taking from HopeFell:** `com.rgs.core`, the `AttackTimeline` pattern, GOAP (M6, only if it beats a plain state machine), the persistence layer.

Design rationale worth preserving:

- **Sim owns its own `CollisionWorld`.** `ISimSystem` forbids scene coupling and `Physics.CapsuleCast` needs a live `PhysicsScene`, so a Unity-physics mover could never run headless. A presentation-side baker fills the box list once at load — it touches Transforms, but outside the tick, so the split holds. This is what makes "assert the jump apex in a unit test" possible.
- **Combat timing is authored in ticks, never animation events.** HopeFell already paid for this: its `ClipEventChannel` carries a VESTIGIAL annotation saying hit/parry/i-frame windows were demoted off clip events onto a fixed-tick `AttackTimeline`, with an explicit *"do not re-attach gameplay decisions to these."* Clip events are for VFX/SFX/footsteps only.
- **One action lock, not a stack.** Takeover needs **both** strictly-higher priority **and** an open cancel window — that is what lets an attack keep its committed frames while a parry still interrupts its recovery. A stack raises questions with no good answers.
- **Position is the feet.** Ledge heights and jump clearances are authored against the ground, and a crouch shrinks the body without sliding the character downward.
- **Overlap is strict.** A body resting flush on ground must not read as intersecting it, or every grounded frame triggers depenetration and the character jitters.
- **Coyote time counts from the last grounded *tick***, not an accumulated float — reproducible rather than frame-rate dependent.

---

## 4. Traps already hit — do not rediscover

1. **`.gitignore` was anchored at the wrong level.** Repo root is one above the Unity project, and root-anchored patterns (`/[Ll]ibrary/`…) matched nothing — `git add .` would have committed all of `Library/`. Fixed by a second correctly-anchored copy inside `Prophecy/`. **Both files are required.** Verify with `git check-ignore -q Prophecy/Library/`.

2. **Tests in `file:` packages silently do not run.** Unity auto-includes tests from *embedded* packages but ignores test assemblies in local/registry packages unless the package is listed in `"testables"` in `manifest.json`. It is **not an error** — the tests just never run and the suite still reports green. This was caught only because `RGS.Core.Tests.dll` was missing from `Library/ScriptAssemblies/` while `Rokkan.Prophecy.Tests.dll` was present. **Add every new shared package to `testables`, then confirm its `*.Tests.dll` appears.**

3. **`-batchmode -runTests` refuses a project an Editor has open.** Symptom: "Multiple Unity instances cannot open the same project." While Unity is running, use `Prophecy → Tests → Run EditMode Tests`, which writes `Logs/test-results.txt`. Batchmode only works with the Editor closed.

4. **Two `SimClock` bugs were fixed during the port** (logged in `MIGRATION-HopeFell.md`): `FixedDeltaSeconds` and every `SimTickInfo` returned the 1/60 *constant* regardless of the clock's actual rate, so `new SimClock(30)` told systems each tick was 1/60 s; and `new SimClock(0)` produced an infinite delta and a silently dead sim. Neither affected the 60 Hz path in use.

5. **`[MovedFrom]` does not protect MonoBehaviour scene references** — those bind by script `.meta` GUID. The shared `Sim` files deliberately reuse HopeFell's original GUIDs so its `SimClockDriver` placements survive migration. **Do not regenerate those `.meta` files.**

6. **Expected console noise:** two red `[RandomSource] GetStream called with null/empty name` lines during test runs. That is HopeFell's own `GetStream_NullOrEmptyName_FallsBackToDefault` exercising the fallback path by design. Not a failure.

---

## 5. Tooling

**Unity's own MCP (`mcp__unity-mcp__*`)** is registered for this project and connected — a stdio relay (`~/.unity/relay/relay_win.exe --mcp`) shipping inside `com.unity.ai.assistant`. Use it for scenes, prefabs, play mode and console reads.

**Coplay is fully removed** — package residue and MCP registration both. Ignore any lingering references.

Registered against `C:\Users\MattS\Documents\RGS\Prophecy\Prophecy` (git root / session cwd). Opening Claude Code directly in the nested Unity folder would need its own registration.

**The one code-generated asset:** `GrayBox_Traversal` geometry, built by an idempotent `[MenuItem("Prophecy/Build/…")]` generator, because it must be *derived from* `MovementTuning` — retune a number, regenerate, and jump gaps stay honest. Everything else (Bootstrap, player prefab, camera rig) is one-off setup: build it directly through MCP.

---

## 6. Next up — M3

Goal: **traversing gray blocks is fun with no art, no enemies, no goal.** Then stop, tune, and lock the numbers — everything downstream is sized from them.

1. **`MovementTuning`** ScriptableObject in `Assets/_Prophecy/Data/` — every number: walk/run speed, accel/friction, gravity up vs down, apex gravity scale, jump height, coyote ticks, buffer ticks, crouch height, down-thrust speed. Authored in **ticks** where it is timing, not seconds. SO edits persist through play mode, so this is the live tuning surface.
2. **Movement modules** on the existing registry — `GroundMove` (build run as a *toggle* first per open knob #1, with an analog-blend switch to A/B it), `Crouch`, `Jump` (coyote, buffering, variable height, apex hang), `FallLand`, `DownThrust`, `TopDownMove`, `Interact`. Register `DoubleJump`, `Crawl`, `DodgeStep`, `LedgeHang`, `LedgePullUp`, `LadderClimb`, `FlameArt` as stubs — **adding each later must not touch an existing module. That is the architecture test.**
3. **Presentation** — `PlayerInputCapture` (Input System → `InputFrame`, latching button edges between ticks so a press+release inside one tick is not lost), `CharacterView` (reads sim state, interpolates by `SimClock.InterpolationAlpha`), `CollisionBaker`, damped follow camera with side-scroll and top-down modes, F1 debug overlay (stance, grounded, velocity, active lock, coyote/buffer timers).
4. **Scenes** — `Bootstrap` (persistent: `SimClockDriver`, player, camera), `GrayBox_Traversal`, `GrayBox_Arena`, `Overworld_GrayBox`; `SceneDirector`, `SceneDescriptor`, `SpawnPoint`, `Portal`.
5. **Input asset** — replace stock `InputSystem_Actions.inputactions` with `Prophecy.inputactions`, one `Gameplay` map carrying the **whole final moveset now**, including abilities that ship disabled.
6. **Tests** — jump apex height, coyote window and landing tick asserted against `MovementTuning` with no scene. Verify identical results at 30/60/144 fps, or the fixed tick is not doing its job.

Cinemachine is **not** installed; a ~50-line damped follow is the plan for the gray box.

---

## 7. Open design questions (raised, not answered)

Flagged to the user during planning; none are blocking M3, all cost money if discovered late.

1. **§5.7 contradicts §7.** §7 promises six bosses on one skeleton "differing by a single signature behavior, not bespoke tech" — but §5.7 specs Mirefen as a **water temple with sluice puzzles** and Cordwell as an **endless-horde arena with a swelling boss**. Two whole subsystems in boss costumes. Either downgrade them or knowingly name them the two expensive fights.
2. **The "lose" ending is a fail state players will reload past.** §4.7's defusing of win=good/lose=bad depends on the loss reading as *an ending* — credits, consumed save, epilogue. A "Retry" prompt means nobody ever sees the cleaner-handed fate.
3. **Power-gating on total power is farmable.** Resolve also comes from ordinary enemies, so a player who spares every Protector can grind to the gate and "your power *is* your complicity" quietly breaks. Either bindings are the only meaningful power source, or the gate reads essences directly.
4. **The dawning arrives too late to be a choice.** §4.6 calls bind-or-spare "the game", but §5.6 puts the dawning at Mirefen, by which point 4–5 of 6 kingdoms are resolved. Suggested fix: make binding **revisitable**, so sparing becomes a standing temptation rather than a one-time choice.
5. **Delay vs. witnessing pull opposite ways.** §5.2 wants the cost distant *and* witnessed. At least one forced return must be authored — Zelda II's death rule (return to start with stats intact) is the free mechanism.
6. **Aldhearth has no Protector** (§5.5) yet §4.7's win is "complete the binding". What is the final essence? The most devastating answer is HopeFell's own populace — the distributed thing, centralised. Unresolved, and it is the climax.
7. **The Seven Tenets may dilute the prophecy** — two reveals of identical shape (holy text meaning its opposite) can blunt each other. Consider the Tenets being *recognised* as a lens rather than *decoded* as a second found-fragment puzzle.
