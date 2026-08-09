# Wukong Phase 1 Product Requirements

## Goal

Build the first maintainable Windows runtime for Wukong without turning the project into a Pupu re-skin or a button-driven animation viewer. Phase 1 proves that one real P0 behavior can be requested by owner input, autonomous scheduling, and model intent through the same deterministic pipeline.

## Required end-to-end flow

```text
InputEvent
-> Intent
-> BehaviorRequest
-> Eligibility (Accepted / Rejected / Deferred)
-> Arbitration
-> BehaviorExecution
-> AnimationLifecycle
-> BehaviorOutcome
-> RuntimeState
-> Event / Memory candidate
-> Developer Trace
```

The UI, menu, scheduler, and model never directly choose a file, play an animation, or write state.

## Phase 1 scope

- A Windows desktop host and input routing adapted only after auditing the pinned Pupu reference.
- Domain models for requests, eligibility, runtime/relationship state, lifecycle, outcome, events, and trace.
- Deterministic eligibility and arbitration with a fixed clock and seed in tests.
- A manifest-driven animation orchestrator supporting `intro`, `loop`, `exit`, `interrupt_exit`, declared fallback, pose compatibility, FPS, direction, anchor, and window movement.
- A minimal autonomous scheduler for quiet/rest P0 behavior.
- Provider-neutral model interface plus a local fake provider.
- Separate Owner conversation and Model Debug conversation sessions.
- Reliable local settings, state, event, and short-term conversation persistence.
- Read-only P0 asset status and developer trace integration in the control panel.

## Six-tab UX contract

| Primary tab | User question |
|---|---|
| Owner | How is Wukong now, and what can I do with it? |
| Profile | Who are Wukong and the owner, and how is the relationship developing? |
| Album | Which photos are worth remembering? |
| Model | Which provider is active, and how does conversation behave? |
| Assets | Which actions exist and what is their real approval/readiness state? |
| Developer | How are events, decisions, execution, memory, and diagnostics implemented? |

Normal mode must not expose raw state injection, forced execution, prompt trace, JSON editors, or internal logs. Developer mode may expose these as diagnostic or simulation capabilities, but simulation is visibly marked, isolated from production state/memory, and has one-click restoration.

## Explicitly out of scope

- P1-P4 action completion;
- vector database or automatic cross-day personality learning;
- full visual understanding of album photos;
- free-roaming/outdoor system, complex toys, festivals, or magic specials;
- automatic generation of missing animation frames;
- macOS, accounts, cloud sync, or online update service.

## Phase 1 acceptance

- Release build and automated tests pass in the actual source tree.
- At least one eligible P0 behavior completes its full lifecycle and one behavior follows a safe interruption path.
- Owner input, autonomous tick, and model intent all traverse the same arbitration service.
- Missing or non-approved assets produce a declared fallback or `Deferred`, never an unrelated random animation.
- Developer trace explains gates, score components, selection, execution, outcome, and state/memory eligibility.
- No Pupu asset or user data is present in application or release outputs.
- Cross-build/static verification and Windows 10/11 real-machine verification are reported separately.
