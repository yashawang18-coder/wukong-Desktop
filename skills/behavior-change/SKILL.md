---
name: behavior-change
description: Change Wukong behavior, state, memory, dialogue intent or autonomy while preserving the shared BehaviorRequest, capability, lifecycle and reducer architecture. Excludes asset import and panel-only styling.
---

# Behavior Change

Use this skill for changes to decisions, Episodes, command willingness, memory
weights, initiative speech, input interpretation, state evolution or action
lifecycle behavior.

## Read First

- `docs/handoff/BEHAVIOR_SPEC.md`
- `docs/behavior-agent-core-v2.md`
- `docs/behavior-continuity-v1.md`
- `docs/readme/05-behavior-model-memory.md`
- Relevant Domain/Application/Desktop tests and the action manifest/contract

## Required Path

```text
InputEvent -> Intent -> BehaviorRequest -> Eligibility -> Arbitration
-> AnimationLifecycle -> BehaviorOutcome -> PetStateReducer -> Event/Memory -> Trace
```

UI, owner menu, autonomous tick and model output submit semantic requests. None
may select a frame path, start a player or mutate formal state directly.

## Workflow

1. Define the behavior's source policy, participation mode, priority, start/end
   posture and detailed pose compatibility, interruption policy, cooldown and
   state effects.
2. Add it to `BehaviorCapabilityCatalog` only when its asset gates and intended
   sources are explicitly valid. Missing capability means `Deferred`, not a
   guessed substitute.
3. Keep all Normal non-idle execution linked to a request/execution ID. Resolve
   outcomes through `PetStateReducer`; reject duplicate, stale and preview
   completion events.
4. Treat Stable Idle as a presentation state, not a completed behavior or a
   repeated memory/reward event.
5. Keep relationship and structured memory as bounded score adjustments after
   hard gates. Free text, album images and LLM text never grant a capability or
   alter approval.
6. Build owner-facing language only from admitted runtime facts. A model may
   suggest language/intent but cannot claim rejected, deferred or unstarted
   actions.

## Invariants

- Preview, simulation and developer-forced execution use isolated state and do
  not write production relationship, learning or memory.
- Do not hard-cut incompatible pose/camera families or commit target posture
  before the matching lifecycle outcome.
- Explicit owner commands are generally cooperative but remain subject to safe
  interruption, pose, asset and state constraints. Magic remains owner-forced
  while still respecting runtime safety.

## Finish

Add deterministic tests for hard gates, seed stability, source policy, stale
completion, preview isolation and state/language consistency. Use trace output
to make the new decision explainable in Developer mode.
