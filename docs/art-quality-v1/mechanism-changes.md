# Companion mechanism first pass

Date: 2026-09-30. Local work only; no asset approval or public release.

## Scope

Reuse `PetAgentState`, `PetStateReducer`, `BehaviorDecisionEngine`,
`PetEpisodePolicy`, and the existing Desktop capability catalog. Do not introduce
a parallel Agent or a second animation player. Wukong remains a companion, not
a work assistant; approved command and magic designs are unchanged.

| Area | Implemented | Boundary |
| --- | --- | --- |
| Pose continuity | Owner admission checks the visual pose family. Existing approved prone-to-sit and sit-to-stand transitions resolve command preparation steps. Missing steps fail closed. Already-sitting/already-prone acknowledgements hold the current frame instead of replaying an incompatible full transition. | No new bridge art, crossfade or pixel editing. Real mixed-sequence continuity still needs Windows review. |
| Continuous daily episodes | Episode availability depends on approved, compatible capabilities; completed episodes have a re-entry cooldown. Exploring can prepare from compatible sitting/side-prone poses one request per Tick. Recovering gets an explicit quiet-idle/posture-transition allowlist. | No command, jump, spin, car or magic added to daily selection. Front-prone cannot invent a get-up bridge. Sleep is not reversed to fake waking. |
| Relationship and memory | Deduplicate source evidence; retrieval labels alone are not facts. Timestamped evidence decays over 90 days, learned preferences over 30 days. Explicit aversion can reduce a bounded theme weight. Owner-completed interactions, not autonomous self-selection, reinforce long-term preferences. | Album mentions are weaker than confirmed conversations. Text classification remains conservative and rule-based. Memory never grants capability or changes approvals. |
| Proactive speech | Suppress while sleeping and for 90 seconds after owner interaction. Explicit short quiet replies reach the reducer, clear pending response and reduce initiative acceptance. Curiosity templates express wishes rather than inventing observed movement. | Existing quiet hours, cooldown, unanswered budget, local fallback and LLM fact validation remain. This is not general sentiment understanding. |
| Command willingness | Repetition penalty is action-specific and expires. Existing energy/stress exceptions and explanations remain; no new random refusal. | Keep command imagery, timing, semantics and owner priority. Magic bypasses willingness, not safety. |
| Outcome integrity | Ignore nonterminal/predating callbacks. Interrupted execution may consume resources but cannot award completion benefits. Failed panel inspection does not add stress or teach preferences. | Existing execution-ID deduplication and preview isolation remain. No claim that every legacy updater has now migrated. |

## Data flow

```text
Owner event / elapsed-time Tick
  -> existing shared state + bounded evidence projection
  -> compatible capability / episode gate
  -> episode dwell + re-entry cooldown
  -> existing deterministic utility scores
  -> BehaviorRequest + existing arbitration
  -> approved pose preparation + original action frames
  -> execution-ID checked outcome
  -> PetStateReducer
  -> compatible stable idle + dialogue projection
```

Old random selection is not a fallback for an authoritative Episode with no
candidate. An active action blocks ordinary autonomous selection even when it
permits owner interruption. Existing episode traces include the keep/switch
reason; ordinary pages are unchanged.

## Evidence and tests

`CompanionQualityTests` covers deterministic 100-seed/10,000-decision admission,
pose/source/approval gates, busy ownership, command repetition, memory dedup and
decay, negative feedback, episode cooldown, partial completion, panel isolation,
and no autonomous reinforcement of long-term preference. Desktop tests use the
actual capability catalog and assert that command preparation contains the
approved transition's real frame sequence. Existing preview, stale callback,
command, magic, coin, car and rendering tests remain in the full suite.

## Remaining work

- Windows owner review of sitting/prone command preparation and 30-minute daily
  continuity; a process launch is not that acceptance.
- Approved wake and front-prone get-up bridges where absent. Fail-closed state
  retention can still look like a long pause without those frames.
- Quality art replacement: the two image-generation attempts failed technical
  constraints and were not imported. Existing walking and sleep art is unchanged.
- More explicit owner preference controls and richer, consented memory evidence
  can follow separately; no broad UX or long-term memory migration is included.
