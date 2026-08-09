# Behavior Core Specification

## State ownership

- `TemperamentBaseline`: stable owner-controlled traits; ordinary interaction never rewrites it.
- `RuntimeState`: energy, fullness, fatigue, stress, curiosity, social desire, play desire, safety, current pose, behavior, and phase.
- `RelationshipState`: trust, familiarity, touch acceptance, and initiative acceptance.
- `LearnedPreference`: behavior/interaction/context evidence; Phase 1 keeps the interface but does not promote complex cross-day habits.

State is changed by domain events and `BehaviorOutcome` only. View models expose commands and read models; they do not use unrestricted two-way binding to domain state.

## Request and lifecycle contracts

`BehaviorRequest` includes request/correlation IDs, semantic intent or existing canonical `behavior_id`, source, time, priority, context, defer policy, and optional fixed seed for developer tests.

Eligibility returns `Accepted`, `Rejected`, or `Deferred` with stable reason code, user-facing reason, and optional retry time. Execution reports `Requested`, `Accepted`, `Deferred`, `Rejected`, `Started`, `Progressed`, `Completed`, `Interrupted`, or `Failed`.

`BehaviorOutcome` records completion ratio, interruption/failure reason, bounded state delta, and memory eligibility. Effects earned before interruption are not rolled back.

## Eligibility and arbitration

Only eligible candidates are scored:

```text
score = BaseWeight
      + StateFit
      + RelationshipFit
      + ContextFit
      + LearnedPreference
      - CooldownPenalty
      - RepetitionPenalty
      - InterruptionCost
      + SeededJitter
```

Every component and gate reason is traced. Minimum dwell, cooldown, repetition suppression, switching hysteresis, pose/asset availability, and safe-interruption rules are mandatory. Seeded jitter resolves ties only. With no legal candidate, return `Deferred`; do not play a random fallback.

## Animation lifecycle

```text
prepare pose -> intro -> loop -> exit -> safe end pose
                         |
                         +-> interrupt_exit -> declared fallback/safe pose
```

The orchestrator is manifest-driven and is the only component that finalizes animation execution. Callers do not separately implement cleanup. Playback failure is converted to `Failed` and a safe fallback without terminating the desktop process.

Preview and simulation use isolated player/state/event contexts and never contribute to real state, relationship, preference, or memory.

## Model boundary

Model output is limited to natural-language reply, optional semantic behavior intent, confidence, and optional memory candidate. Any behavior intent passes through intent resolution, eligibility, and arbitration. The model cannot name an asset path, invent a canonical ID, mutate state, force success, or bypass harness/safety/interruption constraints.

## Minimum automated tests

1. UI and model cannot directly start animation.
2. Identical state/clock/seed gives identical arbitration.
3. Minimum dwell defers unsafe switching; high stress can reject interaction.
4. Cooldown and repetition penalties affect traceable scores.
5. No candidate returns `Deferred`.
6. Normal and interrupted animation paths execute declared phases/fallbacks.
7. Player faults do not terminate the app.
8. Preview/simulation do not mutate production state or memory.
9. Completed, interrupted, and failed outcomes are persisted correctly.
10. Owner and Model Debug conversations use distinct sessions.
