# Wukong Desktop - Codex Account Transfer

## Purpose

This is the working handoff for continuing Wukong Desktop from a different Codex desktop account. It is a reconstructed, high-signal record of owner decisions and implemented repository state. The Codex client does not expose a byte-for-byte chat export to the local coding agent; use this together with the committed repository as the source of truth.

## Start Here: How To Use This Handoff

### What Is Being Migrated

The migration unit is the **entire current local worktree**, not only GitHub. The local candidate changes below are uncommitted, so a fresh clone of GitHub will **not** contain them.

| Situation | Correct action |
|---|---|
| New Codex account on this Windows machine | Open the existing repository directory directly. Do not clone another copy. |
| New Windows machine | Transfer a copy of the repository folder including `.git`, tracked changes and the two authorized untracked directories. Then open that copied directory. |
| Only GitHub is available | Treat it as the published baseline only. Do not claim the latest posture-policy candidate is present until the local worktree or an explicit patch has also been transferred. |

Do not transfer `.publish-check/`, `bin/`, `obj/`, logs or raw source ZIPs as source control. They are disposable build outputs. Preserve `.asset-staging/`, `AGENTS.md` and `skills/` because they are current owner-local inputs/rules.

### Five-Minute Takeover Checklist

1. Open the existing `<repository-root>`, not a newly cloned sibling directory.
2. Read `AGENTS.md`, then this section, then the files in [Read Before Editing](#read-before-editing).
3. Run the following **read-only** checks before editing anything:

   ```powershell
   git status --short --branch
   git branch --show-current
   git rev-parse HEAD
   git diff --check
   ```

4. Confirm the expected branch and committed baseline are `codex/project-skills-foundation` and `f05b16c76abeaed270e7c11127f4b75f5d516e70`.
5. Compare `git status` with [Expected Working Tree](#expected-working-tree). If a change is missing or unexpected, stop and ask the owner before cleaning, switching branches or staging.
6. Launch the [Latest Local Candidate EXE](#latest-local-candidate-exe) for the owner review. A successful launch is not a production approval.
7. Make only the owner-requested change. Before a future commit, run the validation gate in [Verification And Publish Gate](#verification-and-publish-gate).

### Hard Safety Rules

- Never use `git reset`, `git clean`, rebase, force-push, broad `git add .` / `git add -A`, or checkout-overwrite in this worktree.
- Do not modify `main` without a new explicit owner instruction.
- Do not stage `.asset-staging/`, `.publish-check/`, `bin/`, `obj/`, logs, raw ZIPs, portable user settings, chat history, memories or albums.
- Do not use an unapproved, deprecated or pose-incompatible asset as a fallback.
- Do not treat this document as authorization to commit, push, create a PR, merge or change asset approval.

## Current Local Candidate: 2026-10-05

This section is the authoritative snapshot for the local worktree at handoff time. The older dashboard/candidate notes later in this document are historical records only.

### Expected Working Tree

Current branch: `codex/project-skills-foundation`
Current committed HEAD: `f05b16c76abeaed270e7c11127f4b75f5d516e70`

The following changes are intentional and must be preserved:

| Category | Paths | Owner / meaning |
|---|---|---|
| Current candidate source and tests | `src/Wukong.Application/BehaviorAgentFoundation.cs`; `src/Wukong.Desktop/ControlPanelWindow.xaml`; `src/Wukong.Desktop/ControlPanelWindow.xaml.cs`; `src/Wukong.Desktop/DesktopPetRuntime.cs`; `tests/Wukong.Desktop.Tests/Program.cs` | Local posture-policy and debug-history implementation awaiting owner review. |
| This handoff | `docs/handoff/2026-10-04-codex-account-transfer.md` | Local migration instructions; update deliberately, do not discard. |
| Owner-local rules | `AGENTS.md`; `skills/` | Preserve. Not part of the posture-policy scope. |
| Authorized input | `.asset-staging/` | Preserve untouched and untracked. Never stage. |

No entry in the table is published by the current committed HEAD. Do not infer GitHub status from the candidate EXE.

### What The Candidate Changes

| User-visible outcome | Implementation boundary |
|---|---|
| Clear active model/debug conversation history from the pet-setting page | Clears only the selected pet/memory/model debug session. It does not erase portable owner chat history. |
| Prevent autonomous daily behavior from sitting indefinitely | Stable posture dwell is configured per posture, with pose-compatible exits only. |
| Prefer natural prone daily rest | Prone has a longer dwell and normal idle preference; Sit has lower idle preference and a finite dwell. |

The candidate does **not** change PNGs, manifests, asset approval, menus, model authority, action registrations or `main`.

### Posture Policy Defaults

`AutonomousAgentRolloutOptions.PosturePolicies` is the single rollout configuration for these values. It replaces the former Sit-specific runtime timeout.

| Posture | Minimum dwell | Maximum dwell | Next-decision delay | Idle preference |
|---|---:|---:|---:|---:|
| Stand | 14 s | 45 s | 14-26 s | 1.00 |
| Sit | 24 s | 90 s | 30-53 s | 0.55 |
| Prone | 35 s | 480 s | 55-96 s | 1.00 |

At maximum dwell, the runtime changes posture only when a production-approved, current-pose-compatible transition exists. Otherwise it keeps a stable idle instead of hard-cutting or fabricating an action.

### Latest Local Candidate EXE

- EXE: `.publish-check/posture-policy-runtime-candidate-20261004/Wukong.Desktop.exe`
- SHA-256: `9264563B02B1291F6C98A95AD4CE450B30455073A1DEFCC9308253DF85BDAFAC`
- Launch smoke: passed. It stayed alive for five seconds and only its exact process ID was stopped.
- Review now: clear each debug-history session once; observe Stand/Sit/Prone dwell, natural compatible transitions and any busy-lock regression.
- Not yet implied: commit, push, formal release or owner visual approval.

### Verification And Publish Gate

Already completed for this candidate:

- Release solution build: passed.
- Contract validation: `0 errors`, `9 known gaps`.
- Python tests: `95/95` passed.
- C# self-tests: Domain `5/5`, Contracts `5/5`, Application `58/58`, Infrastructure `23/23`.
- Focused Desktop posture-policy and mechanism-dashboard self-tests: passed.
- `git diff --check`: passed.

Known limitation: the aggregate Desktop console runner is **pending**, not passed. Its legacy WPF host can leave a UI thread alive after individual cases complete and therefore never produce a final aggregate result. Keep this limitation visible; repair the test-host lifecycle as a separate focused change rather than masking it.

Before any future commit or push: rerun the focused tests, build, contract validation, Python suite and `git diff --check`; visually review the candidate; then obtain fresh owner authorization for the exact commit/push scope.

## Read Before Editing

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
- Repository location: use the current device's existing `<repository-root>`.
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
