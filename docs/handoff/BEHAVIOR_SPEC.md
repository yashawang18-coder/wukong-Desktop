# Behavior Core Specification

## State model

Do not collapse all state into one mutable personality object.

- `TemperamentBaseline`: owner-controlled stable traits; ordinary interactions do not rewrite it.
- `RuntimeState`: arousal, stress, social desire, play desire, curiosity, fatigue, safety, hunger/needs as applicable.
- `RelationshipState`: trust, familiarity, touch acceptance, and initiative acceptance.
- `LearnedPreference`: evidence keyed by behavior, interaction, and context; promote habits only after repeated cross-day evidence.

## Autonomous selection

Score eligible behaviors from explicit components:

```text
base weight
+ temperament affinity
+ runtime-state fit
+ relationship fit
+ learned preference
+ context fit
- cooldown penalty
- repetition penalty
- interruption cost
+ seeded jitter
```

Log the score components and rejection reasons. Enforce minimum dwell time, cooldown, repetition suppression, switching hysteresis, and context constraints.

## Interaction pipeline

1. Interpret raw input as a gesture (`touch`, `stroke`, `hold`, `drag`, `rapid_tap`, `release`, etc.).
2. Update runtime/relationship state through bounded deltas.
3. Select a response from eligible behaviors.
4. Execute through a lifecycle: `Started`, `Progressed`, `Completed`, `Interrupted`, or `Failed`.
5. Apply long-action effects progressively; interruption does not roll back effects already earned.
6. Record completion ratio and interruption/failure reason.

## Runtime contracts

- Every behavior references a stable `behavior_id`, never a loose filename.
- Asset availability is checked before selection; missing assets trigger a defined fallback.
- `stop` and safety-critical interruption remain available during long actions.
- State mutations are testable without a window or renderer.
- Randomness is seedable for replay and debugging.
- Persistence has schema versioning, migration, corruption recovery, and privacy boundaries.

## Minimum tests

- score components and eligibility;
- cooldown, minimum dwell, hysteresis, and repetition suppression;
- gesture thresholds and conflicting input sequences;
- interruption and partial-progress accounting;
- missing/deprecated asset fallback;
- seeded replay determinism;
- persistence migration and invalid-data recovery;
- platform UI cannot mutate read-only behavior state through invalid two-way binding.

