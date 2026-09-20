# Behavior Agent Core v2

## Scope

This iteration turns the existing Behavior Agent mock into a controlled runtime
foundation. It applies the useful parts of the Pupu-inspired design notes without
copying Pupu implementation details or replacing Wukong's existing behavior,
asset-gate, animation, or desktop-effect pipeline.

The current rollout keeps authority explicit rather than global:

- `Resting`, `Observing`, `Exploring`, and `Sleeping` use Agent v2 allowlists.
- Lifecycle-created owner, eating, drinking, magic, and vehicle Episodes use the
  same formal state and reducer, but are not autonomous decision pools.
- `Socializing` and general `Recovering` selection remain outside the promoted
  autonomous allowlists until their visual routes are reviewed.
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
| Observing | Agent v2 | Compatible stable idles, `HeadLowerTurnV4`, `FrontProneLickV4` |
| Exploring | Agent v2 | Compatible standing idle and approved left/right patrol |
| Sleeping | Agent v2 | Approved sleep entry and compatible front sleep breathing; no fabricated wake path |
| Socializing | Legacy | Existing interaction routing remains unchanged |
| Recovering | Legacy | Existing sleep/recovery routing remains unchanged |

The Resting and Observing bindings are explicit allowlists. Command actions,
jump, spin, eating, drinking, magic, car ride, and patrol cannot enter these
Episodes through tags, fallback, shared assets, or a high utility score.

An authoritative Episode never falls back to the legacy random selector when no
eligible capability exists. It keeps the current compatible stable idle and logs
the gate result. The legacy selector is still evaluated without side effects for
Shadow comparison and cannot submit a second request.

Rollback is configuration-only: remove the affected Episode from
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

Owner-command admission is deterministic. Low-effort commands normally accept;
medium/high-effort commands combine cooperativeness, trust, familiarity, mood,
available energy, stress, and recent repetition. Independence changes initiative
style and frequency, not obedience. Severe low energy/high stress or excessive
repetition can reject a high-effort command; the middle band returns `Deferred`
with a retry time instead of randomly pretending to comply.

## Lifecycle and reducer ownership

Every tracked Normal execution receives a unique `BehaviorRequest.RequestId`.
That ID is passed through `PetMotionRequest`, the WPF player, and completion
callbacks. The reducer ignores duplicate, stale, or mismatched completion events.

`DeveloperPreview` and `PrototypePreview` operate on an isolated state snapshot.
They do not change formal posture, needs, relationship, recent experience, or
memory eligibility. Stable idle display loops do not set `IsBusy` and do not
re-apply outcome effects on every loop.

All Normal non-idle motions now enter an execution-ID-checked reducer lifecycle.
Reviewed daily, posture-transition, observing, sleep, patrol, food/water,
front-prone expression, and owner-command behaviors have explicit outcome
profiles. Other legacy motions receive a compatibility profile derived from their
declared terminal pose and effect; they still settle through the reducer rather
than writing formal state twice. Old completion branches remain in source as a
rollback compatibility layer, but the Normal lifecycle intercepts them.

Desktop-only effect cleanup still owns opacity, position, coins, temporary windows,
and decoded image resources. Posture, pose, busy state, needs, mood, relationship,
recent experience, and learned preferences are reducer-owned.

## Decision formula

```text
final score = base weight
            + temperament affinity
            + runtime-state fit
            + relationship fit
            + bounded learned preference
            + recent outcome memory
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

## Relationship and long-term behavior memory

The formal Agent state is saved atomically to
`WukongData/agent/pet-agent-state.json` and restored on the next launch. Active
execution and busy flags are deliberately cleared during recovery; relationship,
needs, posture, recent bounded experience, and learned behavior preferences are
retained. A maximum of 32 recent events is kept, and learned weights are clamped to
`[-0.15, 0.15]`, so memory influences close choices without bypassing hard gates.

Successful owner interactions slowly raise trust and familiarity. Touch feedback
adjusts touch acceptance, and an owner response after spontaneous speech adjusts
initiative acceptance. Completed, failed, interrupted, and rejected outcomes add
small time-decayed utility components. Free-text conversation memory remains a
dialogue input and cannot directly grant runtime capability or select an asset.

## Initiative speech

Spontaneous speech is evaluated only while the pet is in stable idle, outside quiet
hours, not petrified, and without an expanded chat. Topic scores come from current
needs, Episode, temperament, relationship, and recent topic history. The service
uses randomized check intervals but deterministic admission for the same state and
seed. It applies topic-repeat penalties, a longer cooldown when the previous line
was not answered, and a budget of six initiatives per eight hours. Speech records
`LastInitiativeSpeechAt`; it does not falsely count as an owner interaction.

## Next rollout

1. Run long Windows observation for Episode continuity, stale callbacks, and
   long-lived busy state.
2. Add an approved sleep wake/interrupt bridge before enabling natural wake-up.
3. Promote Socializing and general Recovering only with explicit visual routes and
   allowlists.
4. Replace compatibility outcome profiles with action-specific profiles as each
   remaining legacy effect receives semantic state values.
5. Keep free-text memory outside capability and asset gates; only validated,
   structured preferences may affect behavior scoring.

The final target is one reducer for posture, pose, needs, mood, relationship, and
recent experience, while WPF-only position, opacity, effects, and resource cleanup
remain desktop responsibilities.
