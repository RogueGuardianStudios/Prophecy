# The Legend of Rokkan: Prophecy — Design Document

> **Title:** The Legend of Rokkan: Prophecy — a 2.5D action-adventure built in Unity (3D models cel-shaded via URP, with top-down and side-scroll cameras over axis-constrained gameplay), set in the Rokkan universe and centered on the town of Aldhearth (which the world will come to call HopeFell), as an homage to Zelda II: The Adventure of Link (NES, 1987). Deliberately small scope: old-school, finishable, not an AAA-indie sprawl. The subtitle is not decoration: *Prophecy* names the keystone of the whole story (Section 4.4).
>
> **Status:** living design bible assembled from the design conversation and the provided HopeFell canon (sections 9.3, 10, 11). Sections marked **Canon** are inherited from the broader HopeFell project; everything else is specific to this game. Unresolved decisions are collected in **Open Knobs**.
>
> **See also:** [Gray-Box-Build-Plan.md](Gray-Box-Build-Plan.md) — the implementation plan for the proxy & gray box phase.

---

## 1. What this is, in one breath

This game is the prequel that depicts the cataclysm itself — the era the world's Warding Flames went out. In the larger HopeFell game the player *investigates* what caused the cataclysm. Here, the player *is* the cause. You start exactly where every HopeFell run starts — a small town, a warding flame, monsters to hunt — and the game gently, righteously tempts you toward the single act that ends the world. You do not lose to the darkness. You become it. The hero of this game becomes the first Overlord.

It is a playable cautionary tale. The hunger for power that drives the whole genre is precisely what ended the world, and the game makes you feel that hunger from the inside before it shows you the cost.

## 2. The Thesis

Everything in this document serves one idea: **the win is the damnation.**

- The mechanics reward pushing deeper — more power, more flames held, more loot. Pushing to hold everything is the over-reach that damns you.
- The game must make the world-ending choice feel like winning right up until the moment it lands. Then it has to read as horror, not triumph.
- There is no save-the-world ending, by design. The world falls regardless. The only thing the player authors is the shape of the fall and how much of the blood is specifically theirs.
- No correct answer is rewarded. No audience applauds the choice. (Canon, 11.3: *"That is not a game mechanic. That is a mirror."*)

The keystone that makes the whole tragedy land — sincere antagonists, identity horror, meaningful endings, and a genuine ludonarrative twist — is the **misread prophecy** (Section 4.4). If any single piece of this design is load-bearing, it is that.

## 3. Premise & Setting

**The tagline (larger game):** A cataclysm has happened; all the Warding Flames have gone out. Your town is one of the last bastions still lit, and the outside world wants to snuff it out. You rebuild, strengthen your resolve, push back the dark, relight the flames, and come to understand what caused the cataclysm so it never happens again — unless there's loot to be had.

This game kicks that cataclysm off — the unmaking the world will name **the Darkening**, a Ragnarök-scale event the lore speaks of the way our myths speak of the Flood. The cataclysm is not a climax you race to prevent; it is the condition unfolding while you play, from its first signs at the opening to the fall at the end. (The home town is called **Aldhearth** in this game; the world renames it **HopeFell** after the Darkening — the *fell of hope* curdling into *where hope fell*. This doc keeps HopeFell for continuity and uses Aldhearth where the in-game present matters.) Canon says it happens, so prevention is off the table from the start. What looks like a holding action — relight, protect, push back — is, in truth, the player unknowingly causing the collapse, region by region.

**One world, darkening.** Think *A Link to the Past*, but the shadow world isn't a parallel map you warp to — it's the encroaching state of the map you're already standing on. A region lost to darkness is the shadow world having eaten that tile. By the end the overworld is mostly shadow. The map itself becomes the record of what you did.

## 4. The Narrative Spine

### 4.0 The Prologue — The Onset of the Darkening

This is the only game in the HopeFell series that opens **in the light**. Every other entry begins in the aftermath, the world already dark; this one begins before the fall — a warded, golden, ordinary world: the small town, the warding flame, monsters to hunt, exactly where Canon 10.2 says it all began. That opening brightness is not set dressing. It is the thing the player will spend the game putting out, and the warmth of the first hour is the setup for the whole tragedy.

**Tone discipline (absolute).** The prologue plays it straight — warm, heroic, a classic call to adventure. No winking, no ironic shadow, no foreshadowing nudge to the player. If the game hints early that this will go bad, the tragedy is defused. The horror must be entirely retrospective. (Same discipline as win-as-damnation, Section 4.7.)

**The first sign you did not cause (a rule, not a flavor).** The first sign of the Darkening must be one the player did not cause — a flame goes out somewhere across the world, by no hand of yours. The world is genuinely beginning to fall on its own. This is load-bearing: if the player's own bindings are the only cause from minute one, the cautionary tale curdles into "you were tricked into being evil" — a trap, not a mirror. The Darkening must be a real, external force the hero righteously answers; then the bindings accelerate and complete it. This is the layered mystery (Canon 10.1) built into the opening — surface answer: "a dark force attacks the world, a hero must rise" (true); deepest truth: the reachers turned a survivable affliction into total collapse. The player set out to fight a real apocalypse and became its engine. Worse than being duped, and the only version where the mirror holds.

**Every sign true, every reading wrong.** The opening signs, escalating:

1. A distant flame gutters and dies on the horizon, seen from home; the townsfolk watch it go. *(The undeniable first domino — not your doing.)*
2. The dark grows bold at the edges: monsters closer to the walls than anyone remembers.
3. A survivor brings word that a region's Protector has turned on its own people — the Shadow-of-the-Colossus hook and the call to bind, in one.
4. The Order of the Flame convenes, declares the foretold hour come, recites the verse to the player's face, and names them the Light Bearer. You set out righteous.

**The thesis hidden in the first hour.** Buried in that same warm opening, never flagged: HopeFell's flame is tended by its *people* — a communal rite, a festival of keeping-it-lit, an NPC who says "we hold it together, always have." The player won't register it; they're about to learn that Protector-power is what matters. But it is the thesis stated aloud in the prologue, findable only on the way back (Section 5.4). The recited prophecy does the same — "the light borne home, and kept: tended, and guarded" describes what HopeFell already is, and the player hears marching orders.

**The golden hour (decided — let them live in the light).** The prologue gives a genuine playable calm before the first sign: a short warded stretch in HopeFell where the player learns stance combat and the down-thrust, earns a first Resolve and first flame-art, and helps a couple of townsfolk — all before anything is wrong. The more the player comes to love HopeFell in that hour, the worse it lands when they are the one who dooms it. (The alternative — a cold open dropping the first flame within minutes — was rejected; we chose the slow warmth on purpose.)

**Handoff.** The prologue feeds straight into the first vertical slice (Section 12): the player rides to the first turning region, fights the first Protector, binds it in innocence (Section 4.2), and walks away as the region begins to die behind them — the first gut-punch, and the loop the slice proves.

### 4.1 The Hunt (Shadow of the Colossus)

Each region is held by a **Protector** — a guardian-spirit that is the keystone of that region's Warding Flame (see Section 5.1). The Protectors have begun to turn on their own regions. The cause is not a villain: it is the impersonal bleed of the shadow realm and, increasingly, the player's own wake — the more Protectors you bind, the more shadow leaks through the holes you've torn, and that leak is what corrupts the rest.

The player travels the world and kills the Protectors, then carries their essence home and binds it into HopeFell's flame to grow strong enough to face the next one. **This is the loop.**

The debt to *Shadow of the Colossus* is tonal, not mechanical. Colossi are bespoke 3D climbing puzzles — the single most expensive boss type there is, and the opposite of a small Zelda II homage. We take the soul, not the systems: a handful of 2D side-scroll fights that go down with weight — the music drops out, the region darkens behind you as you walk away. Same devastation, a fraction of the cost.

### 4.2 The Lie

The player binds the Protectors under the belief that they will be **reborn** — healed, stronger. The early Protectors must be bound in innocence: you do them believing the rebirth completely, blessed by your order and the prophecy, feeling righteous. If the truth were available up front there would be no tragedy, only a villain choosing evil.

Crucially, the deception is **the hero deceiving the hero** — not the hero being deceived. The rationalization writes itself, and every clause is reasonable and every clause is a lie:

- The Protector is already poisoned and turning on its own people.
- The region is doomed either way, so this is mercy.
- Letting the essence go to waste would be the real crime.

### 4.3 The Order (sincere, never scheming)

The hero belongs to a religious order — **the Order of the Flame** — that sends them on the quest. The name itself joins the tragedy: everything in this world orbits the flame (the Warding Flames, the Flame stat, the flame-arts), so an order named for the flame that ends the world by misunderstanding the flame is a quiet, grim joke — the Order of the Flame puts every flame out. The order is the version of "the cult" that survives the thesis, on one non-negotiable condition: **it must be sincere.** Not a conspiracy that knows the truth and feeds a holy lie for power — that hands the player an out ("the church used me, I was a pawn"), and the mirror cannot have an out. A sincere faith you *belong to* does the opposite: it removes the out, because it isn't acting *on* you, it's acting *through* you, with your belief.

The order is the machine that takes ambition without wisdom and makes it organized, generational, and sanctified — the reason one person's hubris becomes a doctrine a whole world dies for. It is built around **the third option** (Canon, 11.4: fix everything, permanently, perfectly — relight the entire network for good). That doctrine must fail, because you cannot redeem a distributed thing by hoarding it. The prophecy, as the order reads it, is a contradiction dressed as salvation.

**The Seven Tenets — the sins, unrecognized.** The seven deadly sins are the secret architecture of the descent — never labels on the map (no "Domain of Wrath" over a door; that is allegory as theme park and would gut the tragedy), but a lens the player may recognize in hindsight, exactly like the prophecy. In theology Pride is not one sin among seven; it is the root the other six grow from — and **Pride is the Order itself.** ("Ambition without wisdom," Canon 10.2, was always pride's other name.) Because pride is the sin that blinds you to sin, a sincere Order can enshrine all seven as its holy virtues and never know it: the Order's **Seven Tenets** — its sacred path, named as virtues in-world — are the seven deadly sins, codified in perfect good faith. The same device as the prophecy: holy doctrine that means its opposite. The player names the sins; the Order only ever knew the virtues, and Pride at the root is why it could never tell them apart. **Decision: discoverable** — the Tenets are a real in-world text the player can piece together and decode, a second prophecy-grade reveal, not merely the designer's invisible skeleton.

| Sin (Pride = root, the rest its branches) | Where it lives in the game | The virtue the Order calls it |
|---|---|---|
| **Pride** — we can fix everything | The Order itself; the third option (11.4); the hero's final act, becoming the Overlord — pride consummated. Opens the game and ends it. | Devotion |
| **Lust** — the craving to be the One | The hunger to be the chosen hero — the engine of the misread prophecy (he wants it to be about him, so he reads himself in). | the Calling |
| **Envy** — coveting the guardians' light | Their power is wasted on dying regions; it belongs in worthy hands — the resentment that licenses the taking. | Justice |
| **Greed** — the hoarding | "Make of the many, one"; the centralizing mechanic (5.1); the home of "unless there's loot." | Stewardship |
| **Gluttony** — the binding loop | Essence after essence, never enough, always the next one needed — the progression engine (5.2). | Communion |
| **Wrath** — the killing | Every Protector dies by your blade; violence dressed as kindness (ties to the "mercy" rationalization, 4.2). | Mercy |
| **Sloth** — the despair | The populace losing hope as a region falls — the death spiral is acedia, mechanized (5.2); and the dawning's paralysis, the precursor "unable to stop" (4.5–4.6). | Peace |

Two of these are load-bearing, not cosmetic: **Lust** is the engine of the misread prophecy, and **Sloth** is the death-spiral (hope draining from the people is literally the sin that empties the flame). That they carry mechanical weight is the proof this overlay is structural, not sprayed on. And like everything else here, the sins are de-monstered: medieval art draws them as horned demons; yours wear robes and speak gently — virtues, teachings, reasonable steps. The horror was never going to have horns. *(Optional texture, never labeled: a quiet sin-coloring on a region or two — a place that fell to Greed, a people lost to Sloth.)*

### 4.4 The Misread Prophecy — the keystone

**The prophecy does not predict the future. It causes it.** Everyone reaching to fulfill it is what breaks the world, and the breaking is what the words were "about" all along. The misreading is the engine of the entire tragedy.

**The chain of misreading:**

1. The previous prophesied hero — now the failed precursor (Section 4.5) — believed the prophecy was about him. He reached, and failed.
2. The order read that failure as *wrong vessel*, never *wrong path*. Institutions always do.
3. The order turns to you. You inherit his certainty and his cover story in one motion. You reach, believing exactly what he believed, and the binding-math takes on its blood while you're still sure you're the chosen one.

**The true subject of the prophecy is home.** Read correctly, the words were never about the chosen one who reaches. They are about the light that endures *because* it never reached — the town powered by its people, the flame that outlives every guardian-fed flame you stole, the thing that survives the Reset and ends up older than the world. The prophecy names the community that does not centralize, does not hoard, does not believe the third option. Every reacher who thought it meant *go take the power* read it backwards. It meant *tend what you have.*

**The ludonarrative twist:** the prophecy is "about HopeFell" — and you've been playing HopeFell the whole time. The subject was never a character. It was the ground under your hands.

**Craft requirement (how to land it):** the prophecy text must genuinely sustain both readings in plain sight — lines like "the one who carries the light home," "the flame that does not fall," "born of the last bastion." On the surface: the hero who hauls essences back to power his flame. Underneath, and obvious in hindsight: the town itself. Drop the literal words early; let the hero and the order interpret them triumphantly to the player's face; let the player reinterpret them at the dawning with no narrator explaining the switch. The reveal must be *findable*, not announced (Canon, 10.1: assembled from ruins, raid-lore, and fragments).

**The prophecy text (locked).** What the Order of the Flame recites — the verse the Bearer believes:

> When the wardings fail and the dark comes in to stay,
> when the flames of the world go out, one by one,
> one light shall not be taken.
>
> Not the brightest of them, nor the strongest,
> nor the great one crowned above the rest —
> but the light borne home, and kept:
> tended, and guarded, and never let fall.
>
> It shall gather what the dark has scattered,
> and make of the many, one —
> the scattered flames made safe by their rebirth.
> When the last flame gutters, it shall burn still;
> when the world ends, it shall remain —
> standing at the end as it stood at the beginning,
> older than the ruin, first of what comes after:
> the bearer of the light that does not fall.

**The lost continuation** — eaten by the Darkening (Canon 10.1), found late, deepest in, with no narrator:

> Seek not the hand that carries the light,
> but the hearth it is carried toward.
> No flame you gather is reborn —
> only the world, and only then,
> when the Darkening is done.

**How it carries both readings.** No person is ever named — no *he*, no *she*, no *who*; only "one light," "it," "the bearer." The hero and the Order read themselves into "the bearer" because a prophecy about a chosen person is a better story than one about a place — but the grammar never agreed with them. Three of the Order's load-bearing phrases each invert at the reveal:

- **the bearer** — heard as the hand that carries the light away (the hero); means the hearth it is carried *toward* (the home).
- **make of the many, one** — heard as the third option, the flames collapsed into a single node; means the many gathered into one is the single thing the dark takes in one blow.
- **rebirth** — heard as the guardians reborn "healed, stronger" (the rebirth-lie, and now holy text rather than the hero's private rationalization); means the *world's* rebirth — the first Reset — and only after the last flame is gone.

Read the recited verse again after the continuation and it turns over whole.

**The reveal is found, not hidden — so the Order stays sincere.** A clear text contradicting the doctrine, sitting ignored in the Order's hands, would make them schemers and hand the player the out. So the continuation was *lost*, not suppressed: the Darkening destroyed the warning against the Darkening. The player finds it where the Order never could — deepest in. Layer the reveal so it isn't only found: the player re-reads the verse the Order taught them and feels it turn in place, the fragment merely confirming what the known words were saying all along.

**The hero's title — Bearer / Bringer.** Per the prophecy the hero is the **Light Bearer**: to *bear* is to carry and tend, the humble role the verse actually blesses ("borne home, and kept: tended, and guarded"). The Order's misreading crowns them the **Light Bringer**: not one who tends a light but one who brings it to the dark by reaching, gathering, force. The devout call you the Bearer; the world, as you rise, hails you the Bringer; the gap between the two words is the distance you fall. *(The echo is deliberate — Lucifer is Latin for the light-bearer, lux + ferre, "to carry," the morning star who fell.)*

### 4.5 The Quest-Giver = the Failed Precursor = the Final Boss

The one who sends you out is the previous prophesied hero: he reached, failed, and survived diminished — "recuperating" is both his cover and the literal cost of over-reaching. He is Canon 10.3 made flesh: a person suspended in the moment of his own unfinished catastrophe, still trying to finish what he started because stopping would mean accepting he destroyed everything for nothing. Not evil. Just unable to stop. He sends you out and feeds you the rebirth-lie not to use you, but because **he still believes it.** He is recruiting you into the delusion that already hollowed him out.

He is also the final boss, and the fight is power-gated by your complicity (Section 6.3): bind enough Protectors and you become strong enough to put him down. That single design move gives us a final boss, the thesis, and the fail-states all at once.

### 4.6 The Dawning & the Tension

The engine of the whole game is that by the time you understand what binding the Protectors is doing, you are already in too deep. The dawning is not merely "this is killing the regions." It is total and identity-shattering: **the prophecy was never about me. I was never the savior. I was a stanza.**

After the dawning, the player faces the balance the whole game is built on:

- **Keep binding** → strong enough for the final fight, but you damn yourself and the world further.
- **Start sparing** → cleaner hands, a region or two left breathing, but you walk into the final fight underpowered.

**This balance is the game.**

### 4.7 The Endings

The final confrontation with the failed precursor resolves into two fates. Both end with the Overlord born and the world fallen — exactly as canon demands, with no external villain required.

- **Win (you are strong enough):** you kill him and complete the binding — the third option, poured into HopeFell's flame. The Warding Flame network, concentrated into a single node, collapses in your hands. You become the first Overlord, frozen at the peak of a triumph that was catastrophe. This is the damnation, and it must read as the most horrifying moment in the game — no fanfare, just you becoming the thing.
- **Lose (you spared too many / are too weak):** he stops you too late. He takes what you harvested — the completion he could never reach alone — and *he* becomes the Overlord. The over-reach is done either way and the world falls, but you did not become the monster. The game-over is arguably the more merciful fate for the player's soul.

This inversion — win = horror, loss = the cleaner-handed end — is the deliberate defusing of the "win is good / lose is bad" reflex that a final boss would otherwise smuggle in. (See Open Knobs for the alternative defusing.)

### 4.8 The Thesis, Read Correctly, Too Late

The comprehension that arrives at the dawning is the prophecy finally read right: tend the flame, hold the light, care for part of it without reaching for all of it. This is the larger game's Fortress / Endless Vigil thesis (Section 8, Canon 11.2) delivered as a one-time tragic understanding rather than a repeatable endgame loop. It changes nothing about the world — it is too late — except the player. (Canon 11.4: the cataclysm was caused by someone who could not accept the same impossible choice, who loved the world too much to tend just part of it.)

### 4.9 The First Reset

This game is the first Reset (Canon 9.3). The Reset — "clearing the board, keeping only your town" — is described in canon as making the same choice the darkness makes. This prequel is the first time anyone ever made that choice, at the scale of everything, without knowing that's what they were doing. HopeFell survives its own founder's apocalypse, keeps its flame, and walks out the other side. That is the literal reason HopeFell is older than the world around it in every game that follows.

## 5. Core Systems — Design

### 5.1 Warding Flames, Protectors & Regions

The world is one overworld divided into regions (target ~5; see Scope). Each region has a Warding Flame whose strength is the sum of two fuels:

- **The Protector** — the keystone. Provides most of the flame's power.
- **The populace** — every living person in the region. Each contributes a little; together, a lot.

A flame must clear a **warding threshold** to keep the shadow out. The flame was designed to be *distributed* — many people plus one great guardian, a shared defense. The sin at the heart of the game is not only reaching too far, it is **centralizing**: pulling the keystone out of a communal arch and hoarding it in one node. (Canon 10.2: "reached for something beyond what the Warding Flame system was designed to contain, and the whole network collapsed." The network was meant to be many flames in many hands.)

**Region states:**

| State | Meaning | What it offers |
|---|---|---|
| **Warded** | Lit, safe | Heal, save, NPCs, vendors/loot |
| **Failing** | Dimming, under threat, shadow bleeding in | Last window to act; danger rising |
| **Consumed** | Flame out, region lost | Tiles flipped dark, enemy table swapped, NPCs gone |

### 5.2 The Binding Loop (the heart of the game)

1. Reach a region's Protector and kill it (a side-scroll boss fight with weight; Section 4.1).
2. The Protector's essence is siphoned into HopeFell's flame — a big, satisfying power spike (Section 6.2). You are not collecting trophies; you are concentrating that region's warding power into your own.
3. With the keystone gone, the region's flame drops below the warding threshold. The arch sags. The lesser stones — the people — take a load they were never built to carry alone.
4. The region enters a death spiral on an authored runway: shadow bleeds in → monsters spawn → people die, flee, or despair → populace fuel shrinks → the flame sinks further, faster → **Consumed**.

The decline is slow enough to witness and deterministic. The people put distance and delay between the act and the cost: you kill, you carry the essence home, you feel powerful — and the dying happens later, somewhere you've already left, to people you're no longer looking at. That delay is exactly how someone good at the game keeps going.

**Lost flames stay lost.** Reclaiming a Consumed region on demand is not possible in this game. This is deliberate:

- It gives the binding real weight and the runway real teeth.
- It makes the darkening map = the player's accumulating failure, and the final dark map = the picture of the fall.
- Propping up a dying region would become babysitting busywork and hand the player an out. The only agency is the choice to take the Protector or not.

### 5.3 The Darkening World (scope-safe shadow creep)

- **One overworld, not two.** The shadow world is the *state* of tiles, not a separate map. *A Link to the Past*'s two-world dread, without building two worlds.
- **Authored shrink, not a decay simulation.** Regions fall via the binding-driven runway and authored beats — never a free-running background timer the player babysits. This is hand-authored tension, exactly Zelda II's spirit, and it settles the scope question cleanly: a bounded, timed objective, not an open-ended survival sim.
- A Consumed region swaps its lighting, materials, props, enemy spawn table, and NPC set — **geometry stays**, so it doesn't rebuild, it darkens. Cheap, data-driven, dramatic.

### 5.4 HopeFell endures — because of its people

Every flame you gut was propped on a hoarded guardian. HopeFell's strength was never a great Protector — it's the people. Home believes hardest; its populace fuel runs deepest; its flame holds long after the guardian-fed flames you stole have gone dark. The player spends the whole game certain that Protector-power is what matters, and the light that actually survives is the one powered by community and hope. The distributed human thing endures; the concentrated, hoarded thing falls. This is the thesis whispered through the mechanic, and the literal reason HopeFell outlives the world after the Reset.

### 5.5 The Seven Kingdoms

The world is seven kingdoms: **Aldhearth** at the center (home; later remembered as HopeFell) and six ringed around it, each the seat of a Warding Flame, a Protector, and one of the seven deadly sins (Section 4.3). The dark eats inward from the rim, so the safe map contracts toward home and the final stand is the center going dark last. *(Names under the sins are working names.)*

| Kingdom | Sin / virtue | Biome | Role |
|---|---|---|---|
| **Aldhearth** (→ HopeFell) | Pride / Devotion | the central hearth-town | home; no hunt; the finale |
| **Ashmoor** | Wrath / Mercy | scorched highlands | first binding; tutorial cap |
| **Gildhollow** | Lust / the Calling | a gilded pilgrim-city | belief |
| **Threnhold** | Greed / Stewardship | gem-veined vaults | the loot bind |
| **Lowmarch** | Envy / Justice | a borderland in shadow | the mirror (dark-link) |
| **Mirefen** | Sloth / Peace | a fog-drowned marsh | the dawning |
| **Cordwell** | Gluttony / Communion | a fallen feast-city | the arena; the late mirror |

### 5.6 The Three-Phase Progression

The darkening world is gated into three authored phases — the map degrades in stages as the player wins. Each phase is a global world-state (palette, lighting, enemy tables, NPC overlays) layered over the per-region states of 5.1, and 2.5D makes the shifts cheap: lighting and post-process, not rebuilt geometry. Transitions fire on completion counts. "Completing" a kingdom means resolving its Protector — bound or spared (Section 4.6, Knob #2). The Darkening advances with progress and claims regions regardless; binding makes a region's fall *yours* and feeds power, sparing leaves it to the creep and leaves you weaker. The world reaches Darkened either way.

1. **Lightly Tainted** — begins when the player sets out for Ashmoor. Ashmoor (the tutorial cap) falls, then three kingdoms open: Gildhollow, Threnhold, Lowmarch — the kingdoms that flatter and justify.
2. **Tainted** — triggered by resolving any 2 of those 3. The world tips; the last two open — Mirefen and Cordwell (Gluttony). The dawning begins to creep in; Mirefen is where the player first watches a region die in full.
3. **Darkened** — triggered by resolving all six. The map is mostly shadow. The player returns home to "tend the flame" — the prophecy's true instruction, weaponized — and the final arc begins (Sections 4.5–4.7): the failed precursor, the last binding, the Overlord, the first Reset.

The deep ruin (the lost-prophecy fragment, Section 4.4) surfaces around the Tainted → Darkened turn, as the binding-math goes irreversible.

### 5.7 Towns, Techniques & the Protector Fights

Two principles unify all six guardians:

- **Open it, then strike it.** The opening differs per guardian; damage is always dealt with the normal high/low blade and the down-thrust. Wrath is the one fight with no tool — which is why it caps the tutorial.
- **The town loop.** Each kingdom holds a town whose side quest teaches a martial technique that opens its guardian. Techniques are distinct from the flame-arts of 6.4 (Order/flames) — two kits. Parry is locked as the first technique (Ashmoor, the tutorial cap); the rest of the ability set is a later design pass (Knob #9), so the per-kingdom techniques below are placeholders, not commitments. One traversal item remains the single overworld gate (Section 7): the warding ember/lantern that lets the player push deeper into the more-tainted kingdoms.

Per kingdom (technique → fight; *italic = proposed / open*):

- **Ashmoor (Wrath)** — tutorial cap, technique: **Parry**. The moveset is the fundamentals exam, and parry is its graduation: an overhead slam you jump, lesser hits you block, a low gore you crouch, and a big telegraphed ember-burst you parry at the flash to stagger it open. Damage is always your normal strikes and the down-thrust. As the first kingdom-town, Ashmoor is where parry is learned (matching the "first town = parry" intent); the core defensive kit — jump, block, parry — is all in hand by the time the tutorial ends.
- **Gildhollow (Lust)** — *technique TBD*. Its signature is a blinding radiant beam — a prime target for the parry already in hand (the tutorial skill, reused) — but Gildhollow's own town technique is part of the later ability pass.
- **Threnhold (Greed)** — *Guard-Break / heavy strike*. Crack the gem-plate carapace to expose the dim core (armored high, soft low, a crystal weak point for the down-thrust). The loot bind.
- **Lowmarch (Envy)** — *Dash / Evade*. A dark-link mirror that copies your moves and drains Flame; dash the copied strikes, vary stance so it can't mirror clean, punish the overreach.
- **Mirefen (Sloth)** — a water-temple, not a brawl. Raise and lower the mire via sluices; the climax lowers the sleeping guardian into the water to drown it, barely resisting — the most damning mercy. *(Its town technique tied to the puzzle, or the up-thrust slotted here for later — open.)*
- **Cordwell (Gluttony)** — the arena. An endless-horde arena: waves spawn and the guardian feeds and swells the longer the fight drags, so it can't be out-attritioned (gluttony is never satisfied) — survive the waves, force the window, end it before it bloats past you. The late mirror: by now the player is the glutton too. *(A spin/whirl technique fits the crowd-clear — proposed.)*

## 6. Combat & Progression (the Zelda II layer)

### 6.1 Stance Combat

Side-view sword combat is the signature feel. Preserve the techniques that make it Zelda II:

- High attack standing; low attack crouching.
- The iconic **down-thrust** (jump + hold down + attack to stab below you). Non-negotiable — it's *the* move and it's cheap to build.
- Up-thrust learned later.
- Jump, shield (blocks projectiles by stance), and enemies that guard high or low, forcing stance choice.
- Trigger-collider hitboxes, i-frames, knockback.

### 6.2 Progression — "Resolve" → three stats

Zelda II's experience-and-choice system, renamed to the fiction:

- **Resolve** is XP. It fills a bar.
- At each threshold, the player chooses which of three to raise: **Might** (attack), **Flame** (magic), **Heart** (life).
- Binding a Protector delivers a large, satisfying power spike. The horror only works if the hunger was fun first — getting stronger must feel great. The gap between your swelling power and the darkening map is the entire emotional engine.

### 6.3 Power-Gated Finale

The final fight against the failed precursor is gated by how much you bound (Section 4.5):

- Default lean: gate on **total power**, so the final fight is a measure of the player's complicity, not a loadout puzzle.
- Spare too many Protectors → underpowered → lose → the precursor becomes the Overlord (the cleaner-handed fate).

### 6.4 Flame-Arts (magic)

A **Flame** meter plus a small spell set (Zelda II had eight; we cut to ~3 for scope), learned through the order / at flames and drawn from the Flame meter:

- **Ward** — defense.
- **Flame** — attack.
- **Mending** — heal.

Tying spellcasting to "Flame" links your magic to the same sacred fire you are busy hoarding.

## 7. Scope (holding the line)

Deliberately small. Same engine throughout — one darkening overworld, side-scroll combat, three stats, flame states. The only genuinely new system beyond a base Zelda II homage is the per-region flame/essence model plus Protector bosses. The fall is authored content, not new tech.

- Seven kingdoms (Section 5.5), with Aldhearth as home.
- Six contested flames gated across three phases (Section 5.6); each can be bound or spared — complicity-by-choice (honors the "unless there's loot" clause).
- One true dungeon at the cataclysm's heart.
- Six Protector boss fights, all on one shared combat skeleton (Section 5.7) — they differ by a single signature behavior and theming, not bespoke tech. The upper edge of small, but reachable; Zelda II shipped six palaces on a tiny team. (Render is 2.5D — cel-shaded 3D; lean on a shared toon shader, rigged models with retargeted animation, and modular asset-store kits so environment art doesn't balloon.)
- One gating item (Zelda II's candle/hammer/boots logic, distilled to one) — likely a warding ember/lantern that lets you push through the dark.
- Death returns you to the start with stats intact (Zelda II).

This is finishable. It is not AAA-indie.

## 8. Canon — the broader HopeFell framework

*Faithfully restated from the provided design (sections 9.3, 10, 11). This is the larger game this prequel feeds; the prequel sits inside it as the origin event.*

**9.3 — The Reset.** When pressure becomes unsustainable, the player can choose to reset — blowing away the world except their town. Not failure-and-restart; it is making the same choice the darkness makes (clearing the board), except you keep your flame, memorial wall, crafting knowledge, and town upgrades. You survive your own apocalypse. Your town becomes older than the world around it. *(This game is the first such Reset — Section 4.9.)*

**10.1 — The mystery structure.** The cause reveals itself in layers: a surface answer early (an obvious villain or event), a deeper truth via ruins and raid-lore (the obvious answer was wrong or incomplete), and a deepest truth that recontextualizes everything (it was not done out of malice; it was done by someone trying to solve a problem). No intact libraries, accounts, or records — the story is assembled from what the darkness accidentally preserved, the oldest adventurers, and the flames themselves.

**10.2 — The cause.** Someone looked for more power. They started where every player starts and never stopped, pushing past what anyone thought possible and reaching beyond what the Warding Flame system was built to contain — and the network collapsed. They probably thought they were making the world safer. Ambition without wisdom.

> Your entire game is about players pushing deeper for more power. The lore reveals that hunger is exactly what ended the world. The player is not just uncovering the mystery — they are living the cautionary tale while they investigate it.

**10.3 — The final boss.** Not a monster, not something corrupted — a person, suspended in the moment of catastrophic failure, still trying to finish what they started because stopping would mean accepting they destroyed everything for nothing. Not evil. Just unable to stop. Defeating them is not triumphant — it is merciful. *(In this prequel, that figure is the quest-giver/failed precursor — Section 4.5 — and the player becomes one.)*

**11.1 — The realization.** You cannot save the world. The question stops being *how to win* and becomes *what kind of person you are when winning is not possible.*

**11.2 — The two paths.**

- **The Fortress** — pull back, fortify your town, make your flame the brightest and most protected light left. Stop trying to save everyone; commit to saving something. Your town becomes a sanctuary others make pilgrimages to; the darkness never takes it, but never retreats either.
- **The Endless Vigil** — keep going knowing it is unwinnable, every relit flame eventually going dark again without sustained effort, losing more than you can count, doing it anyway because the alternative is watching people lose their light alone. Heroic in the exhausting, unglamorous, human sense.

**11.3 — No witnesses.** No cutscene, NPC reaction, or achievement. No other player sees the choice. The town looks the same either way; the flame burns regardless.

> You make the choice alone in the dark and then you live with it. That is not a game mechanic. That is a mirror.

**11.4 — Full circle.** The one who caused everything faced the same essential choice. They could have fortified one flame, or accepted the Vigil's limits. Instead they reached for a third option — fix everything, permanently, perfectly — and that ambition broke the world. The cataclysm was caused by someone who could not accept the same impossible choice the player now faces; someone who loved the world too much to tend just part of it. The two choices are the game's thesis statement about what it means to care for something in a broken world.

## 9. Technical Architecture (Unity, 2.5D)

**Two play modes** (Zelda II's dual structure):

- **Overworld** — top-down traversal across the one darkening map; entrances to towns, caves, the dungeon, and Protector arenas; authored encounters.
- **Side-scroll** — all the action: towns, caves, the dungeon, Protector fights, battle screens.

Implement as separate scenes loaded over a persistent bootstrap scene that owns the cross-scene state.

**PlayerState** (singleton or ScriptableObject; survives scene loads; serialized to JSON for save):

- Levels: Might / Flame / Heart.
- Resolve (XP), current/max Health, current/max Flame (magic).
- Lives; known flame-arts; inventory (incl. the one gating item); last overworld position.
- Bound Protectors / essences collected (drives finale power-gate).

**WorldState** (persists alongside PlayerState):

- Per-region state enum (Warded / Failing / Consumed).
- Per-region populace fuel and Protector status → drives flame strength vs. the warding threshold.
- Drives the overworld's appearance on load: each region renders per its current state. Flipping to Consumed = swap lighting/materials/props + enemy spawn table + remove NPC set — geometry stays, so a kingdom doesn't rebuild, it darkens. The three phases (Section 5.6) ride on top as global lighting/post shifts.

**Data-driven content (ScriptableObjects):**

- **Region** — state, tilemap-swap sets, per-state enemy tables, per-state NPC sets, flame-shrine scene reference, Protector reference, populace fuel.
- **FlameArt** — cost + effect. **Enemy** — stats, high/low guard, behavior. **Protector** — boss data + essence reward.
- Authored beats / sieges are triggers that call into WorldState to change a region's state.

**Combat tech:** kinematic or Rigidbody2D controller; trigger-collider hitboxes; damage events; i-frames; knockback; stance state (stand/crouch/air) gating high/low/down-thrust.

**Death:** reload at the start (HopeFell) with stats intact.

## 10. Zelda II Homage Checklist

- Dual-mode structure: top-down overworld → side-scroll areas.
- Stance sword combat: high/low attacks, down-thrust, up-thrust (later), shield, high/low-guarding enemies.
- XP-and-choice leveling: Resolve → pick Might / Flame / Heart at each threshold.
- Magic meter + small spell set (the flame-arts).
- Cryptic, NPC-driven guidance — here, channeled through the order and townsfolk.
- One gating item (warding ember/lantern).
- Death → return to start with stats intact.

## 11. Open Knobs (unresolved — decisions to make as we build)

1. **Is the player literally the 10.3 boss, or "the first of a pattern"?** Lean: the first. It gives a lineage and lets the tragedy rhyme (the order keeps minting heroes, each certain they're the one) rather than resolve. Either works.
2. **Binding optionality.** Landed on soft-optional / balance: you can spare and stay weaker, and the game makes binding feel necessary and merciful every time. Cost: the finale must stay beatable (or losable in-character) across a range of how much you bound. Worth it — complicity-by-choice beats a clean difficulty curve.
3. **Does which Protector you spare matter?** Lean: gate on total power, not a solvable optimal spare-set (which turns the mirror into a min-max puzzle). If "which" must matter, make it narrative (specifically your home's guardian), not a thing players reverse-engineer.
4. **Defusing "win = good / lose = bad."** Primary plan: make the loss the cleaner-handed fate and the win the damnation (Section 4.7). Backup/secondary: make the win read as the single most horrifying moment, no fanfare. Pick one on purpose during implementation.
5. **Prophecy and agency.** The words must be the reason the hero *chooses* to believe — never a track that removes "you could have stopped." Keep faith as motivation, not rails.
6. **Reveal pacing.** Mechanism settled (Section 4.4: a lost-not-hidden continuation, found deepest in, plus a re-read that turns the known verse over in place — caught, never told). The precise beat to surface it stays a tuning question; aim for the moment the binding-math turns irreversible.
7. **Protagonist identity & place in the Rokkan universe.** Title settled — the Light Bearer (what the devout call them) / the Light Bringer (what the world hails as they rise), per Section 4.4. Their specific identity and place in the Rokkan universe are still open. Whoever they are, they are the one who falls — hero and first Overlord, the same person.
8. **The prophecy's actual wording — locked.** Both halves are written into Section 4.4: the recited verse and the lost continuation.
9. **Region & encounter specifics.** The seven kingdoms, the three phases, and the Protector fights are specced (Sections 5.5–5.7). Parry is the locked first technique (Ashmoor, tutorial cap). Open — to design as one ability pass: every other kingdom's technique (Gildhollow, Threnhold, Lowmarch, Mirefen, Gluttony) and where the up-thrust slots in; plus the Order's rites and lore. Depth to author, not architecture.
10. **Title — locked.** *The Legend of Rokkan: Prophecy.* The *Prophecy* subtitle deliberately names the narrative keystone (Section 4.4) — the thing the player misreads, and the thing that turns out to be about HopeFell all along.

## 12. Build Plan — First Vertical Slice

The tone of a tragedy like this lives or dies in feel, so the first milestone should be playable, not paper.

1. **WorldState + the flame/essence system** — region state machine (Warded/Failing/Consumed), the two-fuel flame model (Protector keystone + populace), the warding threshold, and the authored death-spiral runway. One region wired end to end.
2. **Stance-combat controller** — movement, jump, crouch, high/low attack, the down-thrust, plus block and parry (the tutorial defensive kit Ashmoor tests); one enemy with high/low guard; i-frames and knockback.
3. **Ashmoor — the first Protector fight** — the Wrath warden as the slice's boss (the tutorial cap: jump / block / parry, damage via normal strikes + down-thrust), the binding/essence-siphon and its satisfying power spike, and the region beginning to die behind you as you walk away (music drop, lights guttering).

That slice proves the central emotional loop: kill the guardian, feel powerful, watch a place you "saved" go dark. Everything else is built outward from there.
