# Prophecy — session handoff

**Written:** 2026-07-30, the session that built M4's combat spine, the arena, and defence.
**Resume at:** M4 — finishers. All four answers exist and dying is survivable; what is missing is
what winning an exchange gets you.

This is the "where we are and why" document. Standing rules live in `CLAUDE.md` at the repo root
(loaded automatically) — this file does not repeat them. Design canon is `Plans/Design-Bible.md`;
the phase plan is `Plans/Gray-Box-Build-Plan.md`; dev-only settings that must be reverted before
shipping are in `Plans/Release-Checklist.md`.

---

## 1. Current state

| | |
|---|---|
| Branch | `baseline/unity-project-and-design-docs` (**still not merged to `main`**) |
| Tests | **357 passing, 0 failed, 0 skipped** (~4.1 s) |
| Unity | 6000.5.0f1, URP active, Input System only, Cinemachine 3.1.7 |

To put the work on `main`: `git checkout main && git merge --ff-only baseline/unity-project-and-design-docs`

### Commits this session

```
339a7f3  Add the combat timing spine: TickRange, ScalableWindow, AttackTimeline
c435894  Resolve hits and run combos: HitResolver, Hurtbox, ComboRunner
738818e  Wire attacks into CharacterSim: AttackModule, CombatTuning, the target seam
1aa5586  Add the combat arena: a demo you can swing in, and the overlay to read it
7c661a5  Answer the attack: block, parry, i-frames and hit-react, all in ticks
70d362e  Give the down-thrust its blade, and let it bounce itself
40ddd8f  Build the dodge step, the one thing that is intangible on purpose
(this)   Respawn on death, so combat can be lost and then tried again
```

### Three repos are in play

| Repo | Path | State |
|---|---|---|
| **Prophecy** | `RGS\Prophecy\Prophecy` (Unity project nested one deeper) | active, above |
| **Shared packages** | `RGS\Packages` | own git repo, clean — **untouched this session** |
| **HopeFell** | `RGS\HopeFell` | ⚠️ **still untouched, and must stay that way for now** |

Nothing in `RGS\Packages` changed, so `MIGRATION-HopeFell.md` needs no new entries. Combat lives in
`com.rokkan.prophecy` and has not been promoted to a shared package — it should not be until
HopeFell's turn, because the whole point of building it here was to go further than HopeFell did.

---

## 2. What exists

### Combat — `Runtime/Sim/Combat/`

```
TickRange.cs          half-open [Start, End); zero-length is ABSENT, not instantaneous
ScalableWindow.cs     start + duration; the opening tick never moves, only the length scales
AttackHitBox.cs       one damaging volume: window, offset, half-extents, rotation, damage, cover flag
AttackDefinition.cs   phases as three DURATIONS that partition the action; windows laid over them
AttackTimeline.cs     arms once, resolves windows at Arm, reports what is true this tick
HitResolver.cs        batched ImmediatePhysics overlap + CollisionWorld.IsOccluded for cover
Hurtbox.cs            Hurtbox + Attacker, plain structs keyed on an integer id
ComboRunner.cs        chain links gated on the cancel window, with input buffering
ICombatWorld.cs       the target seam: hurtboxes in, HitEvents out, HitResults back
CombatTuningData.cs   the moveset, stance entry points, buffer length, defence numbers, Validate()
Damage.cs             HitOutcome, HitResult, IDamageGate + GateOrder, PendingStun
Vitals.cs             health, owned by CharacterSim so a test can kill something
HitSweep.cs           swing one volume, dedup per box/target/action, notice being parried
```

### Combat — the rest

```
Runtime/Sim/Abilities/AttackModule.cs   the join: lock, timeline, resolve, publish cancel window
Runtime/Sim/Abilities/Block.cs          held guard; stance picks the height; the block gate
Runtime/Sim/Abilities/Parry.cs          a timed action on its own AttackTimeline; the parry gate
Runtime/Sim/Abilities/DodgeStep.cs      a committed step with i-frames; distance authored, speed derived
Runtime/Sim/Abilities/HitReact.cs       picks up a parked stun, force-locks, shoves; the i-frame gate
Runtime/Core/CombatTuning.cs            asset shell over CombatTuningData
Runtime/Presentation/CombatDirector.cs  scene combat world; rebuilds hurtboxes once per tick
Runtime/Presentation/Combatant.cs       hittable: a dummy, or a front for a simulated character
Runtime/Presentation/TrainingAttacker.cs a dummy that swings back on a tick cycle
Runtime/Presentation/CombatDebugOverlay F2: frame data a tick at a time, defence state, GL volumes
Editor/Build/GrayBoxArenaBuilder.cs     generates GrayBox_Arena from the authored reach
Assets/_Prophecy/Data/CombatTuning.asset       the live moveset and defence numbers
Assets/_Prophecy/Scenes/GrayBox_Arena.unity    generated; 9 stations
```

Tests: `CombatWindowTests` 14, `AttackTimelineTests` 18, `HitResolverTests` 16,
`ComboRunnerTests` 14, `AttackModuleTests` 26, `DefenceTests` 30,
`DownThrustCombatTests` 13, `DodgeTests` 21 → **357 total**.

**Not yet built:** no enemies, no AI, no encounter concept — `TrainingAttacker` swings on a timer
and that is all. No animation system. No overworld scene. **Death is a respawn and nothing more** —
deliberately scaffolding, see §7 and the release checklist. `Crawl` and `FlameArt` are the last two
stubs.

---

## 3. Decisions already made — do not re-litigate

Carried forward and still true: **feel first**; **sim owns its own `CollisionWorld`**; **combat
timing in ticks, never animation events**; **one action lock, not a stack**; **position is the
feet, overlap is strict, coyote counts ticks**; **three scenes, not four**; **progression is an
`AbilityLoadout` asset**; **levels are composed in lanes**; **Cinemachine, Follow only, no Aim**;
**hitboxes are sim-side, not trigger colliders**; **hit geometry is `ImmediatePhysics`**; **cover
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
33. **Blocked counts as connected; invulnerable does not.** A down-thrust pops off anything solid it
    struck, and whether the target was hurt is the target's business. Phasing through i-frames is
    not a connection, or the dive becomes a way to hover over anything recently hit.

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
23. **Three copies of a dedup rule is three chances for them to disagree.** The attack module, the
    training attacker and the down-thrust all need the same five steps around a hit. The third copy
    was the moment to extract `HitSweep`; it should have been the second.
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
targets. **Tests**: `Prophecy > Tests > Run EditMode Tests` writes `Logs/test-results.txt`.

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

1. **No dodge.** `DodgeStep` is still a stub, so nothing the player does grants i-frames
   voluntarily — the only i-frames in the game are the ones that follow being hit. The gate and the
   `ScalableWindow` for it already exist; the module does not.
2. **No air dodge.** Grounded only, because it changes what jumping commits you to and that is a
   feel decision worth making deliberately rather than inheriting.
3. **The down-thrust's hit box ignores its own window.** `AttackHitBox` carries `OpenTick`/
   `CloseTick` because every other volume needs them, but the dive lasts until it connects or
   lands, so no tick count could describe it. The fields sit unused and are documented as such —
   worth revisiting if a second unbounded volume ever appears.
2. **Top-down has no collision.** `CharacterSim.Integrate` skips the sweep in `MovementSpace.TopDown`.
3. **No overworld scene exists yet.**
4. **`AttackModule.Modifiers` is settable but nothing feeds it.** No gear system exists, so every
   scalable window currently resolves at its authored length. The seam is there; the source is not.
6. **`Interact` produces a request and a probe box that nothing consumes.**
7. **F1 and F2 overlays ship visible**, gray-box loadout has everything on — release checklist.
8. **`ImmediatePhysics` is validated on Windows only.** No console playback engines installed.
9. **Cross-platform determinism of hit resolution is unproven and probably false.** PhysX contact
   generation is floating point; Switch 2 is ARM. Harmless for single-player, fatal for replays or
   lockstep. Movement never touches PhysX, so the 30/60/144 guarantee is unaffected.
10. **`RequiredStance` is enforced on start but not on chain links.** Crouching mid-combo does not
    stop a stand-only follow-up from firing.
11. **Death is a respawn, and that is a placeholder.** Running out of health puts the player back
    at the spawn point restored, exactly as falling off the level does. Nothing plays, nothing is
    lost, no encounter ends. It exists so combat can be lost and retried while tuning; the real
    rule is a design decision nobody has made, and it belongs with the Protector fights alongside
    the health-economy question the finisher model already raised. `SceneDescriptor.RespawnOnDeath`
    is the switch to turn off when there is a real one.
12. **Defensive state is read as of the defender's last completed tick.** A hit is resolved during
    the *attacker's* tick, so a guard raised on the same tick the blow lands is not yet up. It is a
    deliberate at-most-one-tick lag — the alternative makes the answer depend on which character
    registered first — but it is a real tick of lenience the player never sees and it should be
    remembered before parry windows are tuned tight.

---

## 8. M4 — done, and what is next

Goal: **stance combat that feels like Zelda II, timed in ticks, testable headless.**

### Done

- **The full window decomposition**, which is what made M4 real work rather than a port. Startup /
  active / recovery as a partition, plus absolute-tick ranges for hit windows (plural), i-frames,
  parry and cancel. This is more than HopeFell ever had — its `AttackTimeline` carried one on/off
  hit window and left parry and i-frames in seconds accumulated in `Update()`.
- **Hit resolution** behind a resolver seam: batched `ImmediatePhysics`, per-attack cover through
  the same baked geometry movement uses, team and self filtering.
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

### Next

1. **Enemies.** `TrainingAttacker` is a dummy on a timer, and it is now the only thing standing
   between the combat systems and an actual fight. It needs a `CharacterSim`, a reason to choose
   between its attacks, and an executable state when it is staggered.
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

---

## 9. The arena demo — how to play it

**Open `Assets/_Prophecy/Scenes/GrayBox_Arena.unity` and press Play.** `BootstrapLoader` pulls
Bootstrap in on top and the `SceneDirector` adopts the arena, so there is nothing else to set up.

**Controls:** `A`/`D` move, `S` crouch, `Space` jump, **`J` attack**, **`K` block (hold)**,
**`L` parry**, **`Ctrl` dodge**. Airborne, hold `S` and press `J` for the **down-thrust**.
`F1` movement overlay, `F2` combat overlay.

A neutral dodge steps backwards; holding a direction steers it. It does not turn you round, so a
back-dodge keeps the enemy in front of you.

The **F2 overlay** is the point of the demo. It draws the current attack's frame data one cell per
tick — phases on the top row, hit/cancel/i-frame/parry windows on the bottom, playhead through
both — plus hurtboxes, the armed attack's boxes (bright when live), and the cover ray, all in the
Game view via `GL` rather than gizmos. Combat timing is invisible otherwise: a swing that misses
can miss four different ways and they look identical at speed.

**Six stations, left to right, every distance derived from the authored hit boxes:**

| Station | What it asks |
|---|---|
| 1 `Basic` | does anything connect at all — both attacks reach it |
| 2 `Squat` | too short for the high slash; only the crouching thrust lands |
| 3 `Raised` | above the thrust's ceiling; only the standing slash lands |
| 4 `Cover` | inside the hit box, behind a grate — `StoppedByGeometry` refuses it |
| 5 `Chain` | 240 HP, enough to survive the opener so the follow-up has a target |
| 6 `Ledge` | a lane up, so vertical reach gets checked too |
| 7 `High` | swings back, high — hold `K` standing, or `L` on the beat |
| 8 `Low` | swings back, low — the standing guard does not answer it; crouch first, then guard |
| 9 `Unblockable` | swings back and no guard answers it at all — move, or parry |
| 10 `Pogo` | a ledge and three tough targets: dive, bounce, and try to chain along the row |

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

## 10. Open design questions (raised, still unanswered)

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
