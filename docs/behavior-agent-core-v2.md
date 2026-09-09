# Behavior Agent Core v2

## Scope

This iteration turns the existing Behavior Agent mock into a controlled runtime
foundation. It applies the useful parts of the Pupu-inspired design notes without
copying Pupu implementation details or replacing Wukong's existing behavior,
asset-gate, animation, or desktop-effect pipeline.

The first rollout is intentionally narrow:

- `Resting` is authoritative.
- `Observing` is implemented and evaluated in Shadow mode, but is not yet
  authoritative.
- `Exploring`, `Socializing`, and `Recovering` continue to use the legacy runtime
  selector.
- No asset, manifest, approval flag, owner menu, model permission, or production
  routing is changed by this iteration.

## Runtime data flow

```text
elapsed time / owner event / runtime outcome
                  |
                  v
            PetAgentState
                  |
       +----------+-----------+
       |                      |
       v                      v
 PetEpisodePolicy     DialogueStateProjector
       |
       v
 BehaviorCapabilityCatalog
       |
       v
 hard gates -> explainable scoring -> seeded near-max selection
       |
       v
 BehaviorRequest -> existing runtime gate -> animation lifecycle
       |
       v
 execution-id checked outcome -> PetStateReducer
```

`PetAgentState` is the single source of truth for temperament, relationship,
runtime needs, posture, pose profile, Episode, recent experience, learned
preferences, active execution, and elapsed-time clock state. Desktop properties
remain compatibility projections over this object rather than independent state.

## Time evolution

Needs evolve from elapsed wall-clock time, not from timer frequency. A 60-second
step and sixty 1-second steps produce the same result. Continuous offline catch-up
is capped at five minutes; skipped time is recorded separately and is not converted
into an unbounded hunger, thirst, stress, or energy jump.

## Episodes and rollout

| Episode | Current authority | Current production allocation |
| --- | --- | --- |
| Resting | Agent v2 | Compatible P2/V3R1 stable idles, approved full daily lifecycle, `StandToSit`, `SitToProne` |
| Observing | Shadow only | Compatible stable idles, `HeadLowerTurnV4`, `FrontProneLickV4`; rollout is not enabled yet |
| Exploring | Legacy | Existing patrol behavior remains unchanged |
| Socializing | Legacy | Existing interaction routing remains unchanged |
| Recovering | Legacy | Existing sleep/recovery routing remains unchanged |

The Resting and Observing bindings are explicit allowlists. Command actions,
jump, spin, eating, drinking, magic, car ride, and patrol cannot enter these
Episodes through tags, fallback, shared assets, or a high utility score.

An authoritative Episode never falls back to the legacy random selector when no
eligible capability exists. It keeps the current compatible stable idle and logs
the gate result. The legacy selector is still evaluated without side effects for
Shadow comparison and cannot submit a second request.

Rollback is configuration-only: remove `Resting` from
`AutonomousAgentRolloutOptions.AuthoritativeEpisodes`. No asset or state migration
is needed.

## Participation policy

| Participation mode | Intended use | Willingness behavior |
| --- | --- | --- |
| `ForcedByOwner` | Approved owner-selected magic | Skips personality willingness, but still respects asset, source, posture, and safe-interruption gates |
| `UsuallyCooperative` | Owner commands | Normally accepts; high-effort commands can reject under explicit extreme energy, stress, or repetition limits |
| `StateSensitive` | Owner invitations and touch-like interactions | Uses runtime state and `CommandCooperativeness` where applicable |
| `Autonomous` | Episode candidates | Requires `AutonomousTick`, approved runtime capability, autonomous binding, compatible posture/pose, cooldown, and Episode allowlist |

`CommandCooperativeness` defaults to `0.82`. It is a stable temperament baseline,
not an alias for attachment or independence.

## Lifecycle and reducer ownership

Every tracked Normal execution receives a unique `BehaviorRequest.RequestId`.
That ID is passed through `PetMotionRequest`, the WPF player, and completion
callbacks. The reducer ignores duplicate, stale, or mismatched completion events.

`DeveloperPreview` and `PrototypePreview` operate on an isolated state snapshot.
They do not change formal posture, needs, relationship, recent experience, or
memory eligibility. Stable idle display loops do not set `IsBusy` and do not
re-apply outcome effects on every loop.

Reducer ownership is explicit during migration. The first owned behaviors are:

- P2 full daily lifecycle
- V3R1 full daily lifecycle
- stand to sit
- sit to prone
- prone head-lower/turn V4
- front-prone lick V4

Each behavior keeps its existing action-specific end posture, pose ID, and state
effects. Category defaults do not overwrite those values. All other actions retain
their existing completion code until migrated in a later batch, preventing double
state writes.

## Decision formula

```text
final score = base weight
            + temperament affinity
            + runtime-state fit
            + relationship fit
            + bounded learned preference
            + Episode fit
            + time context
            - repetition penalty
            - posture transition cost
            - interruption risk
            + bounded seeded jitter
```

Hard gates run before scoring. Randomness is limited to the near-maximum band and
cannot bypass source, approval, pose, Episode, cooldown, dwell, interruption, or
window-motion requirements. Equal state, time, catalog, and seed produce equal
scores and selection.

## Next rollout

1. Run independent Windows observation for Resting and inspect pose continuity,
   stale callbacks, and long-lived busy state.
2. Enable `Observing` only after its front-prone and non-front-prone pose routes
   pass the same checks.
3. Migrate Recovering sleep outcomes into the reducer without inventing a wake
   sequence.
4. Migrate Exploring patrol outcomes and window translation separately.
5. Migrate owner command outcomes, then owner interaction outcomes.
6. Keep magic, car ride, coin, and other desktop effects last; their visual cleanup
   remains owned by the desktop effect controller while formal state settlement
   moves to the reducer.

The final target is one reducer for posture, pose, needs, mood, relationship, and
recent experience, while WPF-only position, opacity, effects, and resource cleanup
remain desktop responsibilities.
