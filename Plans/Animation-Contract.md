# Prophecy — the animation contract

**Decided 2026-07-31.** The rules the animation system is built to, and the reasoning that produced
them. `Plans/Animation-Requirements.md` is the shopping list; this is the constitution.

Standing rules in `CLAUDE.md` still apply and are not repeated. The one that matters most here:
**combat timing is authored in ticks, never on animation events.**

---

## 1. The simulation is authoritative. Animation is a view.

Everything below follows from this one sentence, so it is worth being exact about what it means.

The simulation runs headless at a fixed 60 Hz and decides where the body is, what it is doing, and
what that costs. Animation reads that and depicts it. Nothing an animation does may change a
simulation outcome — not position, not timing, not whether a hit lands.

**Consequences that are easy to get wrong:**

- **No root motion, ever.** `IClipInjector` deliberately has no `applyRootMotion` flag. A clip that
  could displace the character would make position a function of frame rate, and the acceptance
  test — identical results at 30, 60 and 144 fps — would fail silently rather than loudly.
- **A dodge's distance is authored in the sim.** The clip depicts 2.2 m of travel because the sim
  moves 2.2 m, not the other way round. If they disagree, the clip is wrong and gets re-timed.
- **No `AnimatorController` state machine.** The sim already arbitrates what the character is
  doing, through modules, `LockPriority` and `ActionLock`. A controller graph would be a second
  state machine describing the same thing, and two state machines eventually disagree. `BodyState`
  is resolved in code, from sim state, and the animator simply plays what it is told.

## 2. Clip events are cosmetic. There is no vocabulary for anything else.

`ClipEventChannel` offers `FootstepL`, `FootstepR`, `SwingVfx`, `PropToggle`, `Spectacle` and
`ClipEnd`. There is no `HitWindowOpen`, and its absence is the enforcement mechanism.

HopeFell reached the same conclusion and kept the channels with a comment asking nobody to use
them. A comment is not an enforcement mechanism. Deleting the enum values makes the mistake a
compile error.

**`ClipEnd` is not an end-of-life signal.** It is a designer's "effects are finished here" marker,
and it is never reached if the clip is interrupted. The end-of-life signal is `ClipHandle.OnDone`,
which fires exactly once whether the clip finished, was hot-swapped, or was cancelled. Cleanup
that hangs off `ClipEnd` leaks on every interrupted action, and actions are interrupted constantly.

---

## 3. Interaction injection — the decision

**An interactable object supplies the animation the player performs on it.** A lever ships the clip
for pulling it; a chest ships the clip for opening it. The player never learns that levers exist.

This is the same principle as *ability modules never reference each other*, applied to content:
adding an interactable must not require editing anything central. It also puts the authoring where
the art is — whoever models the lever authors the pull beside it.

No new system is needed. `IClipInjector.PlayClip` takes any `AnimationClip` and needs no controller
state, so the seam already exists.

### The division of labour

| Owner | Responsibility |
|---|---|
| **The object** | The clip. The anchor transform — where the player must stand and which way they face for the animation to line up. |
| **The simulation** | The action lock. Moving the player to the anchor. **How long the interaction takes, in ticks.** Whether it can be interrupted, and by what. |
| **The animator** | Playing the clip. Nothing else. |

### The rules

1. **The clip never decides that the interaction finished.** Duration is a tick count the sim owns,
   exactly as an attack's is. A chest that opens when its clip ends is the animation-event mistake
   wearing a different hat — the outcome would land at a moment the sim cannot reproduce, and at
   144 fps it would land somewhere else.

2. **The sim moves the player to the anchor; the animation does not.** Rule 1 of this document.
   The object publishes *where*, the sim performs the move, and the clip is authored assuming the
   body is already there.

3. **Object-supplied clips must be Humanoid.** They retarget onto the player's avatar. A clip
   authored against a bespoke skeleton will not fail loudly — it will produce a folded, wrong pose,
   which is worse.

4. **Cancel any injected clip on scene transition.** This is the one that will actually bite. The
   player lives in Bootstrap and persists; world scenes load and unload additively beneath them. A
   clip owned by an unloading scene, playing on a persistent player, is a dangling reference — the
   same silent, transition-only shape as the `Combatant` registration bug of 2026-07-30.
   `SceneDirector` must cancel before it unloads.

5. **Blend time is per-play, not per-character.** An interaction that must place a hand on a lever
   handle wants a near-zero blend; a cancelled attack wants ~0.08 s. One serialized value per
   component cannot serve both.

6. **The object's own animation is its own business.** The chest lid is driven by the chest's
   `Animator`. Only the *player's* half is injected. Two clips, two targets, no coupling.

### What this deliberately does not cover

Interactions that are also combat — a grab, a throw, a finisher on a staggered enemy. Those involve
two characters, a shared timeline, and outcomes that must be simulated. They are a separate problem
and this contract should not be stretched to cover them.

---

## 4. Not floaty, without root motion

Two different problems get called "floaty" and only one is an animation problem.

### Foot sliding is the animation problem

The clip's authored stride does not match the sim's travel speed, so the feet skate. The fix is to
scale playback rate: `playbackSpeed = simSpeed / clipReferenceSpeed`, and to blend between
walk/run/sprint on actual speed so the *pose* suits the speed as well as the cadence.

**Measured 2026-07-31, and the result changed the mapping.** Prophecy moves at 4 m/s "walk" and
7.5 m/s "run"; Synty's walk clip is authored at 1.46 m/s and its run at 2.60. Mapping by name would
need a 2.7x multiplier on the walk and produce a character sprinting on the spot. Chosen by
measured speed instead — Synty's *run* for Prophecy's walk (1.54x), its *sprint* for Prophecy's run
(1.03x) — which lands both near 1.

**The state names are gait tiers, not real-world gaits.** `BodyState.Walk` means "the slower ground
gait", and at 4 m/s that is a jog. Pick clips by measurement, never by matching the word.

**Reference speeds must be authored, because the clips cannot report them.** The clips in use are
Synty's in-place variants — correct for a sim-driven character, and the reason `clip.averageSpeed`
is near zero for all of them. There is no stride length to divide by. So `BodyAnimationSet` carries
a `ReferenceSpeed` per entry, measured once from the RootMotion counterpart and then fixed.

A reference speed of zero means "do not scale" — correct for every non-locomotion state, since
scaling an attack by movement speed would be nonsense.

### Actual floatiness is a tuning problem

Apex hang time, rise-versus-fall gravity asymmetry, acceleration ramp, landing recovery. No
animation fixes a bad gravity number; it only makes it legible. `HANDOFF` §6 records that movement
was derived and never felt against real art — that debt comes due the moment a body is on screen,
and it is paid in `MovementTuning`, not here.

### Foot IK is deferred

It is the last ten percent, and it needs deliberate wiring once `runtimeAnimatorController` is
null. Speed-matching first; measure whether IK is still wanted afterwards.
