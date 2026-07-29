# Prophecy — pre-release checklist

Things deliberately set for development that **must be revisited before shipping**. Each entry
says what was changed, why, and what "done" looks like — a checklist item nobody can act on
because they don't know the reasoning is just a nag.

Add to this the moment you set something for convenience rather than for the player. The cost of
an entry here is thirty seconds; the cost of shipping a debug default is a review.

---

## Settings

### `runInBackground` — turn OFF before release

**Set to ON:** 2026-07-29, during M3.
**Where:** `Edit → Project Settings → Player → Resolution and Presentation → Run In Background*`
(`ProjectSettings.asset`, key `runInBackground`).

**Why it was turned on:** Unity freezes play mode the instant the Editor loses focus, which makes
it impossible to drive a play test from outside the Editor — the sim stalls at `frameCount = 1`
and a perfectly good collision bake looks broken. It is genuinely needed for tooling-driven
verification.

**Why it must come off:** it also governs the shipped build. Left on, the game keeps simulating
while alt-tabbed — enemies keep moving, the fixed tick keeps accumulating, and a player who tabs
out to answer a message comes back dead. For a single-player action game the correct shipping
behaviour is to pause.

**Done looks like:** `runInBackground: 0` in `ProjectSettings.asset`, and a deliberate decision
about what the game does when it loses focus (pause menu? silent pause?) rather than an accident.

### `SENTIS_ANALYTICS_ENABLED` scripting define — decide, don't inherit

**Appeared:** 2026-07-29, in the `Standalone` defines of `ProjectSettings.asset`. Set by
`com.unity.ai.inference` 2.6.1 — Unity's on-device neural-network inference runtime, formerly
named **Sentis**, which is why the define does not match any package name in the manifest.

**What it is:** Sentis / Inference Engine runs ONNX models inside the game on Burst or compute
shaders (the successor to Barracuda). The define gates that package's analytics path, and it is a
*Standalone* define, so it compiles into player builds rather than being editor-only.

**Worth knowing:** `com.unity.ai.inference` is a **depth-0 entry in `manifest.json`**, not a
transitive dependency — `com.unity.ai.assistant` (which ships the MCP server this project uses)
does not declare it in `packages-lock.json`. So it is very likely removable without losing the
Editor tooling.

**Done looks like:** either the package is genuinely used for gameplay ML — nothing in Prophecy
currently is — or it is removed from the manifest, which takes the define with it. Verify the MCP
relay still works after removal before committing to it.

---

## Content

### `AbilityLoadout_GrayBox` is not the shipping loadout

Every ability is switched on so the gray box can be tested. The real game unlocks them over the
course of the story.

**Done looks like:** a separate loadout asset assigned to the player prefab, with the starting
moveset only, and the unlock schedule driven by progression rather than by this asset being
hand-edited.

### `MovementDebugOverlay` must not ship visible

F1 overlay on the `UI` object in `Bootstrap`. It ships enabled and visible-on-start.

**Done looks like:** off by default in release builds — or stripped entirely, since it also
carries an IMGUI dependency that a shipping UI has no other reason to include.

### Stock `InputSystem_Actions.inputactions` is still in the project

The URP template's input asset. `Prophecy.inputactions` replaced it functionally, but the stock
one was deliberately left alone because template scenes and project settings may still reference
it.

**Done looks like:** references audited, then deleted — or a note here saying which asset still
needs it and why.

---

## Verification

### Combat timing at 30 / 60 / 144 fps

Already covered by `MovementTests.Movement_IsIdenticalAt30_60_And144Fps` for movement. The same
guarantee has to hold for combat once `AttackTimeline` exists, and the check belongs in the test
suite rather than in someone's memory.

### `ImmediatePhysics` on every target platform

Combat hit geometry uses `UnityEngine.LowLevelPhysics.ImmediatePhysics` (chosen 2026-07-29 over
hand-rolled AABBs, for rotation). It is a built-in engine module backed by per-platform native
PhysX, so it should ship everywhere Unity does — but this was validated **only on Windows**, and
no console playback engines are installed on the dev machine.

**Done looks like:** `ImmediatePhysicsProbeTests` run green on each shipping target, not just the
editor.

**Separately:** `RepeatedCallsAreIdentical` proves same-binary repeatability, **not** cross-platform
determinism — Switch 2 is ARM, the others x86-64, and PhysX contact generation is floating point.
Harmless for single-player. If replays, ghost data or deterministic netcode ever become a goal,
hit resolution must move back to arithmetic we control. Movement is unaffected either way: it
never touches PhysX.

### The sim must still run headless

`SimArchitectureGateTests` enforces it structurally, but confirm a real headless run before
shipping — the gate proves no type is coupled, not that a build with no display actually starts.
