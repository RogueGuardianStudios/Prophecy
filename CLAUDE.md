# Prophecy — project guide

**The Legend of Rokkan: Prophecy** — a 2.5D Zelda II homage in the Rokkan universe. Cel-shaded 3D over axis-constrained gameplay, top-down overworld + side-scroll action. The player is the cataclysm: binding the Protectors to grow strong is what ends the world.

- **Design bible:** `Plans/Design-Bible.md` (the canonical spec — read before making design calls)
- **Gray box plan:** `Plans/Gray-Box-Build-Plan.md` (proxy & gray box phase, lock-order)

## ⚠️ The repo is nested one level deeper than it looks

```
C:\Users\MattS\Documents\RGS\Prophecy\Prophecy\     <- GIT ROOT
    .gitignore                                      <- repo-level (anchors resolve HERE)
    CLAUDE.md, Plans/
    Prophecy\                                       <- UNITY PROJECT ROOT
        .gitignore                                  <- Unity-level (anchors resolve HERE)
        Assets\  Packages\  ProjectSettings\
```

**Both `.gitignore` files are required.** The repo-root one uses root-anchored patterns (`/[Ll]ibrary/`, `/[Tt]emp/`, …) that resolve against the git root and so never match the nested Unity project. Without the second copy, `git add .` commits the entire `Library/` folder. If you touch either file, verify with:

```bash
git check-ignore -q Prophecy/Library/ && echo ok
git status --porcelain -uall | grep -Ei "Library/|/Temp/|/obj/|\.csproj|\.sln"   # must be empty
```

A clean baseline is ~109 files (Assets + Packages + ProjectSettings). Thousands means the ignore broke.

## Environment

| | |
|---|---|
| Unity | **6000.5.0f1** — this is the target version for shared packages |
| Pipeline | URP 17.5.0, active (`Assets/Settings/PC_RPAsset.asset`) |
| Input | Input System 1.19.0, `activeInputHandler: 1` — **new system only**, old Input Manager is off |
| TextMeshPro | via `com.unity.ugui` 2.5.0 (no standalone TMP package in Unity 6) |
| Cinemachine | **not installed** — gray box uses a hand-rolled damped follow camera |
| Scale | 1 unit = 1 metre |

Vendor folders to leave alone: `Assets/ai.meshy/` (Meshy Bridge plugin, GPL-3.0), `Assets/MeshyImports/` (throwaway test generations), `Assets/TutorialInfo/` (URP template).

## Sibling project — HopeFell

`C:\Users\MattS\Documents\RGS\HopeFell` — Unity 6000.3.0f1, repo `RogueGuardianStudios/HopeFell`, governed by `docs/systems-contract.md`.

**HopeFell is the *next* game, not a live one.** Prophecy ships first and drives the shared packages; HopeFell catches up later. So: refine shared code freely for Prophecy's needs, but **log every shared-package change** in `MIGRATION-HopeFell.md` so the catch-up is a checklist, not archaeology.

Reusable material lives in HopeFell's `Packages/`, **not** its `Assets/`.

### Shared packages — `C:\Users\MattS\Documents\RGS\Packages\` (its own git repo)

| Package | Holds | Status |
|---|---|---|
| `com.rgs.core` **0.2.0** | `SerializableGuid`, `TagMask128`, RNG streams, `ISerializer`/`JsonSerializer`, `PersistentSingleton`, `VectorMath`, `DeterministicMath`, **`RGS.Core.Sim`** (the 60 Hz fixed-tick spine) | ✅ extracted, consumed |
| `com.rokkan.core` | `SavePrimitives.cs` (`ISaveable`, `IBind<T>`) | ✅ extracted, consumed |
| `com.rokkan.animation` | The clip-injection system | ⏳ planned M4 |
| `com.rgs.goap` | GOAP (currently in HopeFell's `Assets/`) | ⏳ planned M6, only if it beats a plain state machine |

Consumed via `file:../../../../Packages/<name>` in `Prophecy/Packages/manifest.json`. The path is relative to the **Packages folder** and is four levels up because of the nested layout — if Unity reports a package cannot be resolved, that depth is the thing to check.

**These were copied, not moved.** HopeFell was mid-work (branch `foundry/docs-tdd`, 15 uncommitted changes) and is byte-for-byte untouched; it keeps its own embedded copies until its turn comes. **Log every shared-package change in `RGS/Packages/MIGRATION-HopeFell.md`** — that file is HopeFell's adoption checklist and the only thing preventing its catch-up becoming archaeology.

The shared `Sim` files deliberately reuse HopeFell's original `.meta` GUIDs so its existing `SimClockDriver` scene references resolve after migration. **Do not regenerate those `.meta` files.**

## Architecture rules

### Sim / presentation split — binding

Inherited from HopeFell's contract §6.2 and adopted deliberately:

> **Simulation is plain C# on a fixed tick and must run headless. MonoBehaviours read sim state and capture input; they never decide gameplay outcomes.**

- Sim systems implement `ISimSystem` — no `MonoBehaviour`, `Transform`, `Animator`, `Camera`. Tick at 60 Hz, read `SimConstants.FixedDeltaSeconds`, **never** `Time.deltaTime`.
- Because a capsule-cast mover can't run headless, **sim owns its own `CollisionWorld`** (plain AABBs). A `CollisionBaker` fills it from scene colliders once at load — outside the tick, so the split holds.
- Presentation interpolates between the last two sim states; 60 Hz sim renders stepped otherwise.
- Acceptance test: the sim runs headless. If it can't, the split is broken somewhere.

### Combat timing is authored in ticks, never animation events

HopeFell learned this the expensive way — its `ClipEventChannel` carries a **VESTIGIAL** annotation: hit/parry/i-frame/block windows were demoted off animation events onto a fixed-tick `AttackTimeline`, with an explicit *"do not re-attach gameplay decisions to these."*

- Hit windows, parry windows, i-frames, cancel windows → **authored tick counts** on a timeline.
- Animation event channels → VFX, SFX, footsteps, visual sync **only**.

### Ability modules never reference each other

Modules talk only through the controller, which arbitrates via **action locks** (`LockFlags { Move, Turn, Jump, Attack, Defend }` + priority + cancel window). Adding a new module must not require editing an existing one — that's the architecture test. Cancel windows read sim elapsed ticks, never frame time.

### Animation

`AnimationSystem` hijacks the `Animator` via a `PlayableGraph` and **nulls `runtimeAnimatorController`**. Consequence: direct `Animator.SetFloat`/`SetBool` calls become *silent no-ops*. Always route through the `IClipInjector` parameter facade.

`PlayClip` returns a `ClipHandle` — a per-play subscription scope. Subscribe there, never to global events. `RaiseDone` fires exactly once per play (natural end, hot-swap, or cancel).

## Conventions

- Game code lives in `Assets/_Prophecy/` (underscore sorts above vendor folders) or in `Packages/com.rokkan.prophecy/`.
- Assembly definitions: `Rokkan.Prophecy` (runtime), `Rokkan.Prophecy.Editor`, `Rokkan.Prophecy.Tests` (EditMode).
- Namespaces: `Rokkan.Prophecy.*` — `Sim`, `Sim.Abilities`, `Combat`, `World`, `Presentation`, `Core`.
- 4-space indent, `_camelCase` private fields, `[SerializeField]` over `public`.
- Tuning numbers live in ScriptableObjects (`MovementTuning`, `CombatTuning`), never as literals in code — SO edits persist through play mode, so they are the live tuning surface.

## Unity assets are built by committed Editor scripts, never hand-authored

Scenes, prefabs, materials, `.asset` files and `.inputactions` are fragile generated formats with GUID cross-references. **Never create or edit them with `Write`/`Edit`.** There is no Unity MCP bridge in this project (Coplay was removed), so the standing pattern is:

> Write a C# Editor script under `Assets/_Prophecy/Scripts/Editor/Build/` with a `[MenuItem("Prophecy/Build/…")]` entry point that constructs the asset via `AssetDatabase` / `EditorSceneManager` / `PrefabUtility`. The user runs the menu item; the generated asset is committed alongside the script that made it.

This is better than hand-placement for this project specifically: the gray box scenes are supposed to be *derived from* `MovementTuning` (jump gaps sized off run speed, ledges at hang height). A generator script makes that literal — retune the numbers, regenerate the scene, and the level geometry stays honest. Generators must be idempotent: running twice produces the same result.

C# source is the exception — write that directly.

## Verification

```bash
# Prophecy sim gate (headless — a sim system touching a Transform must throw)
Unity.exe -batchmode -runTests -projectPath <Prophecy> -testPlatform EditMode -assemblyNames Rokkan.Prophecy.Tests

# One-time, after extracting shared packages: prove the move was faithful
Unity.exe -batchmode -runTests -projectPath <HopeFell> -testPlatform EditMode -assemblyNames Rokkan.Gameplay.Tests
```

Combat timing must produce identical results at 30, 60 and 144 fps — if it doesn't, the fixed tick isn't doing its job.
