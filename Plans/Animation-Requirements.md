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

## 1. The Hero rig — cleared 2026-07-31

**Current asset:** `Assets/MeshyImports/T-Pose Figure_20260731_110125/Meshy_AI_T_Pose_Figure_biped_Character_output.fbx`

The first export (`…_102144`) was an unrigged static mesh — zero deformers, zero bones, no bind
pose — and nothing on this list could have been applied to it. The re-export is rigged and set up
correctly:

| Check | Result |
|---|---|
| Skin clusters / bones / bind pose | 24 / 48 / 1 |
| Import rig type | `animationType: 3` (**Humanoid**), `avatarSetup: 1` (create from this model) |
| Rig import errors | none |
| All 15 required humanoid bones | present |

Bone naming is the Mixamo/Maya convention, which Unity's auto-mapper resolves natively:

```
Hips · Spine · Spine01 · Spine02 · Neck · Head
LeftShoulder · LeftArm · LeftForeArm · LeftHand        (+ Right)
LeftUpLeg    · LeftLeg · LeftFoot    · LeftToeBase     (+ Right)
```

`Spine01`/`Spine02` map to Chest and UpperChest; shoulders, neck and toes are optional bones that
happen to be there, which is better than the minimum.

**Being Humanoid is the part that matters**, and not only for this mesh. Humanoid is the
abstraction that lets a clip authored on one skeleton drive a different one, so it is what makes
the Synty library usable at all — a Generic rig could only ever play clips authored on its own
exact hierarchy. The first export was Generic; this one is not.

**One caveat, stated plainly:** the evidence above is the importer's configuration and the
skeleton's contents. The proof is a retargeted clip actually playing on the body without limbs
folding, and that cannot be run until there are clips (§3). Expect to spend a pass in
**Configure → Muscles & Settings** regardless — auto-mapped avatars routinely need shoulder and
elbow limits nudged, and an AI-generated mesh is likelier than a hand-rigged one to have an
off-axis joint.

One harmless import warning to ignore: the FBX carries an empty placeholder clip
(`Armature|Armature|clip0|baselayer`, 0 frames). It is Meshy's export scaffolding, not a rig fault.

*(The counts above were taken against a known-good rigged Meshy export as a control, which reported
the same profile. That control — the Emerald Guardian test generation — has since been deleted
along with the other superseded imports, so the comparison is recorded here rather than repeatable.)*

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

### What was copied, 2026-07-31

A curated subset, not the library: **25 clips and 2 rigs, 11 MB**, in
`Assets/_Prophecy/Animation/Synty/`. The full packs are 963 clips and 712 MB, most of it eight-way
strafe sets for a camera-relative 3D controller that Prophecy has no use for.

```
_Rig/        PolygonSyntyCharacter.fbx              (locomotion source avatar)
             PolygonSyntyCharacter_SwordCombat.fbx  (combat source avatar)
Locomotion/  Idle_Standing, Idle_Crouching, Walk_FwdStrafeF,
             Run_FwdStrafeF, Sprint_F, Crouch_FwdStrafeF
InAir/       Jump_Idle, InAir_FallShort, InAir_FallLarge,
             Land_Idle{Soft,Medium,Hard}
Combat/      Attack_LightCombo01{A,B}, Attack_HeavyStab01,
             Block_{Begin,Loop,End}, Parry_F,
             Hit_{F,B}_React, Hit_F_Stagger,
             Dodge_B, DodgeRoll_F, Death_F_01
```

**Non-RootMotion variants throughout.** The sim owns position, so a clip that carried displacement
would be fighting it — see `IClipInjector`. Synty ships both variants of most clips and only the
plain ones were taken.

**Two rigs, which is the trap in a partial copy.** Locomotion clips reference
`bfa42ba6…` and combat clips reference `68a29e70…` — two different source avatars that happen to
share the filename `PolygonSyntyCharacter.fbx`. Copying only the first left every combat clip
pointing at an avatar that did not exist here. Both are now present, renamed on disk so they can
coexist, with their original `.meta` files so the GUIDs the clips reference still resolve. Verified:
all 25 clips resolve to a rig inside this repo, and the importer logs no avatar errors.

`SYNTY-LICENSE.MD` sits beside them. **The generative-AI clause is still worth your reading** — the
EULA forbids use "with Generative AI Programs" and the Hero is a Meshy generation. Retargeting a
purchased clip onto an AI-generated mesh is not obviously what that clause is aimed at, but it is
close enough that it should be your call and not an assumption buried in a commit.

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
`BodyStateInputs`, and drives the injector. It was left until there was a rigged mesh to prove it
against, because it is the one piece whose correctness is a picture rather than an assertion —
the rig now exists, so the only thing still in its way is having clips to play (§3).
