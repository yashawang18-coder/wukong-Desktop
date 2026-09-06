# Behavior Agent v1 foundation

## Goal

Establish one canonical in-memory Agent state, elapsed-time state evolution, a runtime capability catalog, and a deterministic episode decision engine. Promote `Resting` as the first authoritative episode while keeping `Observing` behind an explicit QA switch and preserving legacy authority for all other episodes.

## Current behavior and evidence

- Baseline: `agent/car-prone-runtime-release-v1` at `4c486484ac3e24b0b9c839245f2c6d5e2538f9d0`.
- Existing `BehaviorAgentMockEngine` models temperament and needs, while `DesktopRuntimeHost` separately owns the production autonomous scoring formula and directly updates state by asset batch.
- Conversation projections currently use separate default personality and relationship snapshots.
- The food/water candidate worktree contains unrelated uncommitted changes and is not used by this task.

## Scope and non-goals

- Add a canonical `PetAgentState` and deterministic `PetStateReducer`.
- Add behavior participation, capability, episode, rollout, and deterministic-decision contracts.
- Project dialogue personality, relationship, posture, mood, and current action from the canonical state.
- Carry one execution ID through requests and renderer callbacks, and migrate Resting/Observing outcome settlement to `PetStateReducer` without double writes.
- Make `Resting` authoritative; keep `Observing` opt-in until its separate Windows QA completes.
- Do not change assets, manifests, runtime approvals, production menu routes, or model permissions.
- Do not commit, push, merge, or modify `main`.

## Architecture impact

`Elapsed time / correlated lifecycle event -> PetStateReducer -> PetAgentState -> BehaviorDecisionEngine -> episode rollout gate -> request or shadow trace`

`PetAgentState -> DialogueStateProjector -> read-only dialogue DTOs`

The Desktop layer executes the new choice only for an authoritative episode. It still evaluates the legacy selector without side effects for trace comparison. Non-authoritative episodes retain the legacy request path.

## Implementation phases

- [completed] Add canonical state, reducer, capability and decision contracts.
- [completed] Adapt Desktop runtime state ownership and elapsed-time updates.
- [completed] Add episode hysteresis, shadow decision trace, and dialogue projection.
- [completed] Add execution correlation, exact pose IDs, stale/duplicate completion protection, and preview state isolation.
- [completed] Promote Resting with an explicit allowlist and add an opt-in Observing rollout.
- [completed] Add deterministic 10,000-decision tests and run the complete validation suite.

## Validation

| Layer | Command or observation | Expected | Actual |
|---|---|---|---|
| Static | `git diff --check` | clean | passed |
| Contract | `python tools/validate_contracts.py` | 0 errors | passed; 9 known lifecycle gaps |
| Python | `python -m unittest discover -s tests -v` | pass | 72/72 passed |
| C# | all five console suites | pass | Domain 5/5; Contracts 5/5; Application 38/38; Infrastructure 19/19; Desktop 86/86 |
| Build | serial `dotnet build Wukong.sln --configuration Release` | pass | passed; 5 existing warnings |
| Windows runtime | controlled Release launch | alive and responsive after 5 seconds | passed; exact PID stopped, no residual process |

## Rollback or recovery strategy

Clear `AutonomousAgentRolloutOptions.AuthoritativeEpisodes` to return every episode to shadow-only operation. No asset, manifest, approval, menu, or model-permission rollback is required.

## Risks

- Divergent pose names from older manifests must remain fail-closed in the capability projection.
- Preview and developer runs must not mutate production state.
- Dialogue DTO compatibility must be preserved for local stores and existing JSON.

## Decision log

- Use the latest runtime release baseline because it already contains the approved daily, sleep, patrol, command, magic, and car-ride behavior metadata.
- Preserve `BehaviorAgentMockEngine` as a compatibility donor during migration; production code does not switch to it.

## Progress log

- 2026-09-06 `completed`: isolated clean worktree created; current state and Pupu reference architecture inspected.
- 2026-09-06 `completed`: canonical state, reducer, episode policy, capability-first shadow scoring, and full dialogue projection implemented.
- 2026-09-06 `completed`: Release build, five C# suites, contract validation, Python tests, and diff check passed.
- 2026-09-06 `completed`: Release executable remained alive and responsive for the five-second controlled launch; PID 28888 was stopped and no exact-path process remained.
- 2026-09-06 `completed`: Resting became the sole authoritative selector for its explicit allowlist; Observing was implemented as an opt-in rollout and remains shadow-only by default pending 30-minute Windows QA.
- 2026-09-06 `completed`: request IDs, exact pose IDs, behavior-specific outcome profiles, stale/duplicate callback rejection, failure settlement, and Preview isolation were added.

## Completion criteria

- One Desktop-owned canonical Agent state backs temperament, relationship, needs, posture, episode, and clock.
- Equal elapsed time produces equal state evolution independent of tick subdivision.
- Capability gates exclude unapproved or source-incompatible behaviors before scoring.
- Same state, clock, and seed produce the same explainable decision.
- Conversation state is projected from the same canonical state.
- Resting has one authoritative request source; unpromoted episodes keep existing production selection.
- Duplicate, stale, Preview, and failed lifecycle events cannot double-write formal state.
