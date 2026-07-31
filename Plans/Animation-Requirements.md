# Prophecy — required animations

**Started:** 2026-07-31, from the moveset as it actually exists in code.

This list is generated from `BodyState` (`Runtime/Presentation/BodyState.cs`), which the resolver
maps sim state onto. That direction matters: the enum is the authority and this document follows
it, so an animation that no state resolves to is one nobody needs, and a state with no clip is a
visible hole rather than an oversight.

> **Combat timing is never authored here.** Hit, parry, i-frame and cancel windows are tick counts
> on `AttackTimeline`. A clip's length has no effect on when an attack connects — if the two
> disagree, the clip is re-timed, not the frame data. See `CLAUDE.md`.

---

## 1. The blocker: the Hero has no skeleton

`Assets/MeshyImports/T-Pose Figure_20260731_102144/Meshy_AI_T_Pose_Figure_0731142135_texture.fbx`
is an **unrigged static mesh**. It contains no bones, no skin weights and no bind pose:

| Probe | Hero | A rigged Meshy export, for comparison |
|---|---|---|
| `Deformer` | 0 | 26 |
| `Cluster` (skin clusters) | 0 | 24 |
| `LimbNode` (bones) | 0 | 48 |
| `BindPose` | 0 | 1 |
| `Hips` / `Spine` | 0 / 0 | 3 / 9 |

The comparison column is `MeshyImports/Emerald Guardian_20260721_095650/…_withSkin.fbx`, which
proves the probe finds a skeleton when there is one. Unity agrees: the importer recorded
`internalIDToNameTable: []` and `clipAnimations: []`, and the asset is set to `animationType: 2`
(Generic) with `avatarSetup: 0` (no avatar).

**Nothing on this list can be applied to that file.** Skinned animation needs bones to move. The
fix is upstream of Unity — re-export the Hero from Meshy with rigging enabled (the "withSkin"
naming on the older export shows the feature has been used before), then set the importer to
**Humanoid**, not Generic.

**Humanoid is not optional if the clips are coming from Synty.** Humanoid is the abstraction that
lets a clip authored on one skeleton drive a different one. Generic rigs only play clips authored
on their own exact bone hierarchy, so a Generic Hero can play nothing but animations made
specifically for it.

---

## 2. The list

Priority: **P0** the character is unusable without it · **P1** needed before the gray box reads as
a game · **P2** polish, or waits on a system that does not exist yet.

Synty columns record what was actually found in the two packs in HopeFell
(`AnimationBaseLocomotion`, `AnimationSwordCombat`) — see §3 for the licence question before
assuming any of it can be used.

### Locomotion

| `BodyState` | P | Loop | Synty | Notes |
|---|---|---|---|---|
| `Idle` | P0 | ✅ | ✅ Locomotion/Idle | Neutral, weapon sheathed and drawn are two different idles eventually |
| `Walk` | P0 | ✅ | ✅ `A_Walk_Fwd_*` | Side-on only; the strafe set is 3D locomotion Prophecy has no use for |
| `Run` | P0 | ✅ | ✅ `A_Run_Fwd_*`, `A_Sprint_*` | Threshold is `BodyStateResolver.DefaultRunThreshold`, 4.5 m/s |
| `CrouchIdle` | P0 | ✅ | ✅ `A_Idle_Crouching_*` | Crouch is a real stance with its own attack, not a transition |
| `CrouchWalk` | P1 | ✅ | ✅ Locomotion/Crouch | |

### Air and traversal

| `BodyState` | P | Loop | Synty | Notes |
|---|---|---|---|---|
| `JumpRise` | P0 | ✅ | ✅ `A_Jump_*` | Split at the apex — one "air" clip reads as floating |
| `Fall` | P0 | ✅ | ✅ `A_InAir_Fall*` | |
| `Land` | P0 | ✗ | ✅ `A_Land_Idle{Soft,Medium,Hard}` | Edge-triggered off `LandedThisTick`, one tick |
| `WallSlide` | P1 | ✅ | ❌ **none** | Zero matches in either pack |
| `WallJump` | P1 | ✗ | ⚠️ | Reuse `JumpRise` mirrored until authored |
| `LedgeHang` | P1 | ✅ | ❌ **none** | |
| `LedgeClimb` | P1 | ✗ | ❌ **none** | Must fit `LedgePullUpTicks` (12 ticks = 0.2 s) or be time-scaled |
| `LadderIdle` | P2 | ✅ | ❌ **none** | |
| `LadderClimb` | P2 | ✅ | ❌ **none** | Needs to read at any speed — the sim drives position |

### Defence

| `BodyState` | P | Loop | Synty | Notes |
|---|---|---|---|---|
| `Block` | P0 | ✅ | ✅ SwordCombat/Block, 13 clips | Held pose; height comes from stance |
| `Parry` | P0 | ✗ | ✅ 18 clips match "Parry" | Same button as block — only the window's timing separates them |
| `HitReact` | P0 | ✗ | ✅ SwordCombat/Hit, 19 clips | Directional variants exist and are worth using |
| `Dodge` | P0 | ✗ | ✅ SwordCombat/Dodge, 16 · "Roll" 16 | **Root motion must be stripped** — the sim owns the distance |

### Offence

| `BodyState` | P | Loop | Synty | Notes |
|---|---|---|---|---|
| `AttackStandA` | P0 | ✗ | ✅ Attack, 42 clips | `slash_high` |
| `AttackStandB` | P0 | ✗ | ✅ combo sets are authored as A/B/C | `slash_high_2` — the chain link. Must differ visibly from A or the combo reads as a stutter |
| `AttackCrouch` | P0 | ✗ | ⚠️ "Stab" 8 clips, none crouched | `thrust_low` — a low thrust from a crouch is the Zelda II signature and probably needs authoring |
| `DownThrust` | P0 | ✅ | ❌ **none** | The pogo dive. Loops because it lasts until it hits something — no clip length can describe it |

### Terminal

| `BodyState` | P | Loop | Synty | Notes |
|---|---|---|---|---|
| `Death` | P1 | ✗ | ✅ SwordCombat/Death, 8 clips | Death is currently a respawn placeholder — see HANDOFF gap 10 |

---

## 3. What Synty can and cannot cover

Two purchased packs sit in HopeFell's `Assets/Synty/` — `AnimationBaseLocomotion` (721 clips,
554 MB) and `AnimationSwordCombat` (242 clips, 158 MB). Both are **Humanoid** (`animationType: 3`)
with a shared source avatar, so they retarget onto any humanoid rig.

**They cover combat and flat-ground locomotion well, and Prophecy's traversal not at all.** The
gap is not incidental — these are third-person 3D action packs, and Prophecy is a 2.5D platformer.
Searching both packs for every traversal term returns nothing:

```
Ladder 0   Climb 0   Hang 0   Ledge 0   WallSlide 0
```

So **six states have no source clip**: `WallSlide`, `LedgeHang`, `LedgeClimb`, `LadderIdle`,
`LadderClimb`, `DownThrust`. Those are the ones to commission, generate, or hand-author. The
down-thrust especially — a pogo dive is a Zelda II signature and nothing in a modern sword-combat
pack resembles it.

Conversely, most of the 963 clips are irrelevant: the locomotion pack is dominated by eight-way
strafe sets for a camera-relative 3D controller, and Prophecy moves along one axis.

### Three things to decide before any of it is copied

1. **Licence.** Synty's EULA grants use in "Products under your direct control" — Prophecy
   qualifies — but forbids sharing outside your team, and explicitly forbids use "with Generative
   AI Programs". The Hero is a Meshy generation. Retargeting a Synty clip onto an AI-generated
   mesh is not obviously the thing that clause prohibits, but it is close enough to be worth your
   reading rather than my assumption.
2. **Repo weight.** 712 MB of licensed binaries committed into the Prophecy git repo is close to
   irreversible and would dwarf the ~109-file clean baseline. A curated subset — perhaps 25 clips
   — covers everything on this list that Synty can cover at all.
3. **Retarget target.** Copied clips carry `avatarSetup: 2` (copy-from-other) and reference the
   Synty source avatar by GUID, so a partial copy needs that rig asset alongside the clips or the
   humanoid setup breaks on import.

**Nothing has been copied.** That decision is yours.

---

## 4. What exists in code already

- `com.rokkan.animation` — the clip-injection package: `IClipInjector`, `AnimationSystem`
  (PlayableGraph, two-input mixer), `ClipHandle`, `ClipEventChannel`, `ClipEventSO`.
- `Runtime/Presentation/BodyState.cs` — the enum this document is generated from.
- `Runtime/Presentation/BodyStateResolver.cs` — pure, static, allocation-free: sim state in, one
  `BodyState` out. Precedence mirrors `LockPriority`, so what is shown cannot contradict what the
  simulation arbitrated.
- `Tests/Editor/BodyStateTests.cs` — 17 tests, weighted toward the precedence cases (hit during a
  swing, dodge through a held guard, a climb that outlives its attachment) because the single
  mappings are self-evident on sight and the orderings are not.

**Still to build:** the `MonoBehaviour` that reads a `CharacterSim` each frame, fills
`BodyStateInputs`, and drives the injector — deliberately left until there is a rigged mesh to
prove it against, because it is the one piece whose correctness is a picture rather than an
assertion.
