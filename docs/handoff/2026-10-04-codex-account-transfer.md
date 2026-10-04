# Wukong Desktop - Codex Account Transfer

## Purpose

This is the working handoff for continuing Wukong Desktop from a different Codex desktop account. It is a reconstructed, high-signal record of owner decisions and implemented repository state. The Codex client does not expose a byte-for-byte chat export to the local coding agent; use this together with the committed repository as the source of truth.

## Open First

1. `AGENTS.md`
2. `CURRENT_STATE.md`
3. `DECISIONS.md`
4. `ASSET_STRUCTURE.md`
5. `docs/handoff/BEHAVIOR_SPEC.md`
6. `docs/handoff/AUTONOMOUS_INTERACTION_ROADMAP.md`
7. `docs/handoff/ASSET_PLAN.md`
8. `docs/handoff/2026-10-04-owner-conversation-record.md`

Read the relevant `asset.json`, manifest and focused test before changing an asset or runtime route. Repository files and actual EXE behavior outrank this handoff.

## Repository And Current Work

- Repository: `https://github.com/yashawang18-coder/wukong-Desktop`
- Local repository used for this handoff: `D:\【ZS】\【桌面宠物】\images_wk\magic\wukong-Desktop-food-water-v2`
- Branch before this handoff commit: `codex/decision-memory-speech-command-v2`
- Published parent commit: `3c072fcb2ce527d4bb02cbe079b28ae02986e406`
- `main` is protected. Do not modify it without direct owner authorization.
- `.asset-staging/` is authorized local input. Keep it untracked, untouched and out of commits.

## Product Intent

Wukong is a companion-only desktop Shiba Inu, not a productivity assistant or animation button. He is an autonomous, affectionate pet with a consistent real-pet identity. He may talk briefly, request ordinary life activities, react to the owner and retain bounded relationship/memory effects, but he must not write code, do work, impersonate an AI assistant or claim an action that runtime did not admit.

Owner-facing language is short, simple Chinese and dog-like. Do not expose technical action IDs or version labels outside Developer mode.

## Architecture That Must Stay Intact

```text
input / menu / dialogue / scheduler
  -> BehaviorRequest
  -> intent and capability eligibility
  -> arbitration
  -> execution and animation lifecycle
  -> BehaviorOutcome
  -> PetStateReducer
  -> event, bounded memory and developer trace
```

- UI, menu, model and autonomous tick never directly play a PNG or mutate formal pet state.
- Model output can express a semantic intent or language only. It cannot choose asset paths, bypass gates, approve assets or mutate formal state.
- Only Normal execution updates formal state and relationship. `DeveloperPreview`, `PrototypePreview` and simulation are isolated.
- Every action keeps source, pose, lifecycle, approval, source-policy, interruption and fallback gates.
- Missing, deprecated or incompatible assets return `Deferred`/`MissingAsset`; never substitute unrelated imagery or old deprecated frames.

## Participation And Priority Policy

| Behavior kind | Participation | Key rule |
|---|---|---|
| Magic special | `ForcedByOwner` | Safe-preempt or wait for a safe point; never autonomous/model/command driven. |
| Owner command | `UsuallyCooperative` | Usually accepts; low energy, high stress or repetition may defer or reject. Stop always works. |
| Owner invitation | `StateSensitive` | Car ride and future outings remain in regular eligibility. |
| Autonomous daily | `Autonomous` | Capability catalog, pose compatibility, Episodes, dwell, cooldown and bounded utility decide. |

`BehaviorCapabilityCatalog` remains mandatory before any action enters an autonomous pool. Assets without production approval never become candidates merely because a demand such as hunger is high.

## Agent State And Decision Design

- Stable posture: `Stand`, `Sit`, `Prone`; `CurrentPoseId` distinguishes front-prone, side-prone, sleeping and other visual compatibility families.
- Long-term temperament: Activity, Attachment, Sensitivity, Independence, Mischief and command cooperativeness.
- Runtime state: energy, hunger, social need, boredom, stress, mood, arousal, current action, repeat count and busy state.
- Relationship and bounded structured memory can affect utility only after hard gates. Free-text album/dialogue content is untrusted and cannot create a capability.
- Decision scoring is explainable: base weight + temperament + runtime state + relationship/memory + context - cooldown - repetition - transition cost + bounded seeded randomness.
- Initiative speech is rate-limited, quiet-hour-aware, fact-checked against active posture/action and responsive to unanswered lines.
- Owner-facing card execution is an inspection action and must not teach relationship, preferences or personality.

## Current Runtime And Asset Boundaries

- Approved assets are not interchangeable by posture. Never hard-cut between front-prone, side-prone, sleep camera or walking direction families.
- Deprecated/rejected material remains immutable audit evidence but has all runtime gates closed and must not be fallback imagery.
- Car ride is an owner-triggered approved runtime feature only, never an autonomous, dialogue, model or command behavior.
- Magic keeps owner-only high priority and restores the pre-effect scale, anchor, posture and state.
- Walk uses approved whole-window translation and one local scale, obeying work-area boundaries and direction/facing policy.
- Food/water currently follow owner-only gates. Do not invent autonomous food behavior until their runtime policy permits it.
- Sleep and wake actions need compatible pose paths. Never reverse sleep-entry frames to fabricate waking.
- The owner dislikes backward-looking side-prone imagery as a dominant daily posture; front-prone is normally preferred.

## Current Dashboard Work

The final local change expands the Developer `动作机制与统计` page using real session-only accepted Normal trigger data, not an analytics service.

- Metric cards: total accepted actions, autonomous actions, owner actions and distinct actions.
- Family summaries: autonomous daily, walking/patrol, sleep recovery, owner command, owner interaction, play/car ride, magic and candidate preview.
- A fixed two-hour, twelve-bucket activity trend with a line chart and per-bucket bars.
- Family distribution bar chart with count, share, average interval and source composition.
- Filterable action detail table; canonical diagnostics remain available only in Developer mode.
- Session trigger history is bounded to 256 events. Stable idle and all preview modes are excluded.

Files changed:

```text
src/Wukong.Desktop/DesktopPetRuntime.cs
src/Wukong.Desktop/ControlPanelWindow.xaml
src/Wukong.Desktop/ControlPanelWindow.xaml.cs
tests/Wukong.Desktop.Tests/BehaviorTruthRuntimeTests.cs
tests/Wukong.Desktop.Tests/Program.cs
```

The focused dashboard self-test succeeds. The full Desktop console runner has an older WPF test-host issue: it creates an `Application` on one finished STA thread and later constructs a control panel on another STA thread, which blocks. Do not claim its aggregate passed until this test harness is repaired in a focused task.

## Candidate EXE

- Directory: `.publish-check/mechanism-dashboard-v1/`
- EXE: `.publish-check/mechanism-dashboard-v1/Wukong.Desktop.exe`
- SHA-256: `23D84DC68EBFE907D0FD972AD6A838FEE34B98E61F7B892D132C2A23B8BDA7FA`
- A hidden five-second controlled launch stayed alive and its exact PID was terminated. This is a launch check, not owner visual approval.
- To inspect: open Developer mode, choose `动作机制与统计`, then trigger several real owner/autonomous actions. Charts are session-local and start empty after launch.

## Dashboard Validation Snapshot

- Release solution build: passed.
- Contract validation: 0 errors, 9 known lifecycle gaps.
- Python tests: 95/95 passed.
- C# self-tests passed: Domain 5/5, Contracts 5/5, Application 58/58, Infrastructure 23/23.
- Focused Desktop dashboard and dialogue-episode-routing self-tests passed.
- `git diff --check`: passed.
- Publish succeeded; it emitted four nullable warnings in `MainWindow.xaml.cs` and no errors.

## Local Data And Release Policy

- User settings, conversation history, album data and memory follow the portable EXE layout but must never be uploaded unless explicitly requested.
- Do not commit `.publish-check/`, `bin/`, `obj/`, temporary preview output, logs, raw ZIPs or `.asset-staging/`.
- Only old directories within `.publish-check/` are eligible for local redundant-build cleanup. Never remove source assets without a named owner decision.

## First Commands In The New Account

```powershell
git status --short --branch
git branch --show-current
git fetch origin
git log --oneline --decorate -5
git diff --check
```

Then read the files named at the top and inspect the actual working tree. Preserve all untracked user files. Do not reset, rebase, force-push, overwrite owner changes or modify `main` without fresh explicit authorization.
