# Behavior Agent v1 Foundation

## Purpose

This change migrates Desktop-owned scoring fragments toward one explainable Agent state and decision model. Rollout is episode-scoped: `Resting` is authoritative in the first batch, `Observing` is implemented behind an explicit option for separate Windows QA, and all other episodes keep the legacy selector authoritative. The new engine still records a side-effect-free comparison choice for every evaluated episode.

No asset manifest, approval state, menu route, or animation binding changes in this phase.

## Data Flow

```text
elapsed time / owner input / lifecycle outcome
-> PetStateReducer
-> PetAgentState
-> PetEpisodePolicy
-> BehaviorCapabilityCatalog
-> hard gates
-> utility scoring with seeded bounded jitter
-> episode rollout gate
-> authoritative BehaviorRequest or shadow-only comparison
-> DeveloperTrace comparison

PetAgentState
-> DialogueStateProjector
-> read-only model context
```

The model receives current state only after the local runtime has established it. It cannot select assets, change state, choose preview modes, or bypass gates.

## Canonical State

`PetAgentState` owns:

- slow temperament, including `CommandCooperativeness`;
- relationship state;
- realtime posture, exact visual pose, energy, hunger, thirst, social need, boredom, stress, mood, arousal, comfort, focus, busy state, active action, execution ID, phase, and stable anchor;
- current episode and minimum dwell;
- bounded recent experience;
- bounded learned preference hooks;
- applied elapsed time and skipped offline duration.

Compatibility properties in `DesktopRuntimeHost` project into this state. They are no longer independent copies.

## State Evolution

`PetStateReducer` uses elapsed time, not callback count. Eight one-second advances produce the same result as one eight-second advance. Resume gaps are capped at five minutes of continuous need evolution; the remainder is recorded as skipped offline time instead of creating artificial care debt.

Lifecycle outcomes have explicit semantics:

- each accepted request carries one `RequestId` through `PetMotionRequest`, the WPF player, and the lifecycle callback;
- start establishes busy state, action, execution ID, phase, interruptibility, and timestamp;
- completed commits the declared end posture and full state effects;
- interrupted applies only proportional body-state cost and does not commit the planned end posture;
- failed clears the active action, adds bounded stress, and preserves the last stable posture;
- long-term relationship changes occur only after a completed owner interaction.

The reducer accepts a terminal event only when its execution ID and behavior match the active execution. Duplicate, stale, and late callbacks are ignored and traced. `DeveloperPreview` and `PrototypePreview` restore an isolated snapshot instead of changing formal posture, needs, relationship, or recent experience. Stable idle loops are display state: they do not set `IsBusy` and do not repeatedly apply outcome effects.

`BehaviorOutcomeProfile` keeps behavior-specific end posture, exact end pose, state effects, interaction semantics, memory eligibility, and partial-effect policy. `ReducerOwnedBehaviorIds` is the migration boundary: listed behaviors settle only through the reducer, while not-yet-migrated behavior families retain their existing completion branches without double writes.

## Capability Before Preference

`BehaviorCapabilityCatalog` projects the loaded runtime catalog into decision metadata:

- participation mode;
- allowed sources;
- start and end posture;
- effort;
- allowed episodes;
- approval and runtime-use gates;
- autonomous binding;
- interruption and window-motion support;
- dwell, cooldown, base weight, and state effects.

Unavailable assets and source-incompatible actions are removed before scoring. Missing food, drink, command, magic, or travel capability therefore cannot dominate the utility calculation and repeatedly fail afterward.

## Participation Modes

| Mode | Intended use | State refusal |
| --- | --- | --- |
| `ForcedByOwner` | Explicit owner magic or system-level showcase after hard safety gates | No mood-based refusal |
| `UsuallyCooperative` | Owner commands | Only clear low-energy, high-stress, or repeated-command exceptions |
| `StateSensitive` | Owner invitations such as car ride | May decline in a clearly poor state |
| `Autonomous` | Approved daily behavior | Selected only by local capability and utility rules |

`Deferred` means the behavior cannot run now because a capability, source, posture, cooldown, or safe-interruption requirement is unmet. `Rejected` means the capability exists but the pet's state expresses a real refusal. These outcomes must not be conflated.

## Episodes And Hysteresis

The first episode policy supports `Resting`, `Observing`, `Exploring`, `Socializing`, and `Recovering`.

- low energy or high stress selects recovery;
- high social need plus attachment selects socializing;
- high boredom with sufficient energy selects exploring;
- curiosity or focus selects observing;
- otherwise the pet rests.

Minimum dwell prevents threshold oscillation. Critical energy or stress may enter recovery immediately. An active behavior holds the current episode until its lifecycle completes.

## Utility Scoring

After hard gates:

```text
final = base weight
      + temperament affinity
      + realtime state fit
      + relationship fit
      + bounded learned preference
      + episode fit
      + time context
      - repetition penalty
      - transition cost
      - interruption risk
      + seeded jitter
```

Only candidates near the maximum score participate in the final seeded draw. The same state, time, capability catalog, history, and seed produce the same result. Randomness cannot bypass a hard gate.

## Dialogue Consistency

`DialogueStateProjector` now supplies personality, command cooperation, relationship, posture, current action, episode, busy state, energy, hunger, thirst, mood, arousal, stress, social desire, play desire, curiosity, fatigue, and safety from the same Agent state used by decision diagnostics.

This prevents a standing pet from describing itself as prone because the conversation layer loaded a separate default snapshot.

## Rollout Boundary

`AutonomousAgentRolloutOptions` controls authority by episode. The first production configuration is:

```text
ShadowEnabled = true
AuthoritativeEpisodes = { Resting }
LegacyFallbackOnInfrastructureFailure = false
```

For `Resting`, the new engine is the only request source. If no eligible approved behavior exists, the pet keeps its compatible stable idle; the runtime does not fall back to a legacy random choice. The legacy selector is still evaluated without mutating counters, cooldowns, state, or request queues, solely for trace comparison.

`Observing` has its own allowlist, exact-pose gates, deterministic tests, and an opt-in authoritative switch. It remains non-authoritative by default until a separate 30-minute Windows visual run confirms that head-turn and front-prone lick transitions do not hard-cut between incompatible prone views. `Exploring`, `Socializing`, and `Recovering` remain legacy-authoritative.

Clearing `AuthoritativeEpisodes` is the rollback. It restores shadow-only selection without reverting state contracts, assets, manifests, or approval records.

## First-Batch Scope

`Resting` may select only compatible stable idles, approved stand-to-sit and sit-to-prone transitions, and approved P2/V3R1 lifecycle motions. `Observing`, when explicitly enabled for QA, may additionally select the approved V4 head-lower/turn and front-prone lick micro-events only from their matching pose families. Neither episode may select sleep, patrol, owner interaction, command, jump, spin, magic, car ride, food, or water behaviors.
