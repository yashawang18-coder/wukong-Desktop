# Walk v7 Review and Execution Settlement

Date: 2026-10-02. Local, uncommitted work on
`codex/decision-memory-speech-command-v2`, baseline
`cc32bb7093707df7c09223d869c842d9cd85be1d`.

## Art Deliverable and Limits

The local-only review is
`.publish-check/art-rebuild-walk-v7-start-stop-review/review.html`.
The batch contains 13 unique native image-generator PNGs: the 8-frame gait
and 5 additional start/stop poses. Cycle frames 1, 5 and 7 are byte-identical
to walk v5. Frames 2, 3, 4, 6 and 8 are whole-frame revision candidates.
No head/neck composite was introduced. Original and rejected attempts remain
preserved. `generation-prompts.json` records selected sources and prompts.

| Timeline | References | Duration |
| --- | --- | --- |
| intro | stand, shift, low reach, cycle contact 1 | 1060 ms |
| loop | cycle 1 through 8, 200 ms each | 1600 ms |
| exit | cycle contact 1, final step, settle, stand | 1440 ms |

A future safe interruption must reach contact 1 before exit. This is a review
timeline, not a newly installed runtime interrupt implementation.

All 13 files decode, their copied bytes match source hashes, and the 5 review
package tests pass. The raw canvas is 1254 x 1254, not the required 1024 x 1024.
Generated low-alpha background artifacts remain. No deterministic pixel cleanup
or resizing was applied; permission for a preserved-original, uniformly normalized
runtime copy was requested and remains unanswered at this checkpoint.

Color-region diagnostics improved some frames but not all of 3/4/6; those
diagnostics cannot certify temporal fur consistency. Owner loop/start/stop review
is still required. Do not treat generation, file integrity, or an attractive
still frame as completed gait repair. Every approval/runtime field remains false.
No generated walk or sleep image is in the asset registry or candidate EXE.
Browser automation could not open the local review URL due to its URL policy;
no automated browser animation/screenshot acceptance is claimed.

## Implemented Code Slice

`DesktopPetRuntime.CompleteMotion` now validates the current request ID and
behavior before handling ordinary outcomes, previews, and terminal holds.
Normal finite outcomes use only the existing `PetStateReducer` path. Removed
the unreachable legacy batch-specific completion mutations and the Desktop
call site of `BehaviorAgentMockEngine.ApplyOutcome`.

Duplicate completions after settlement, completions after Stop/Failed, and
old holds/previews cannot interrupt a newer action or award state benefits.
Infinite idle presentation still records no activity or repeated benefit.
The existing action-specific profiles/effect values were not changed.

Real WPF frame decode/missing-frame errors now queue a correlated Failed
callback after the current frame tick. The callback cannot cancel a newer
request. Effect cleanup stays in Desktop, while normal outcomes settle once
in Reducer. Failed previews restore isolated state without production learning.
This does not claim every display/configuration state assignment has migrated.

## Verification

- Release solution build passed. A full source compilation retains 6 existing
  nullable/unused-local warnings; the final incremental build emitted 2.
- Domain 5/5; Contracts 5/5; Application 58/58; Infrastructure 23/23;
  Desktop 120/120. Total 211/211, including 9 new runtime regressions.
- The WPF regression constructs the real MainWindow, requests an owner action,
  injects a nonexistent frame, pumps Dispatcher and checks Failed rather than
  Completed. No source PNG is corrupted to create the error.
- A 1800-second virtual-clock continuity test passed with bounded experience,
  finite action settlement and repeated stale callbacks. This is NOT a
  30-minute Windows animation or memory soak.
- Python asset suite 87/87; contract validation 0 errors, 9 existing gaps.
- `git diff --check` passed; existing CRLF normalization warning is unchanged.
- All suites ran without a model or network dependency. Desktop verification
  used an isolated WUKONG_DATA_ROOT and disabled legacy private-data migration.

Local folder candidate:
`.publish-check/lifecycle-settlement-v1-candidate/Wukong.Desktop.exe`.
It contains the code slice above but deliberately NOT the raw walk v7 art.
Do not move the EXE without its managed DLLs, resources and runtime dependencies.
Published resource verification: 1678 files, including 1463 PNGs, all SHA-256
matches to source. The isolated candidate survived 20 seconds (PID 5884).
CloseMainWindow returned false; only that path-verified PID was terminated and
confirmed absent. This is startup survival, not graceful Exit or animation QA.
EXE SHA-256: `6a2ff785020dfd21cc537d2a47a9b89b5c445a96283ca2d41d49379d18b2e15d`.
Managed DLL SHA-256: `d2c5c4bc47d58194dd5f87187647be2b270b7c673b4cac6b32d809194eb01a84`.
The unchanged native host EXE hash alone does not identify the managed code version.

## Still Open in the Requested Sequence

1. Walk material/edge/canvas acceptance and approved runtime normalization.
2. A complete new sleep entry/wake/safe-exit sequence. Three accepted breathing
   poses alone are not a complete sleep lifecycle; reverse playback is not approval.
3. Resource import, candidate-only animation wiring, and Windows art acceptance.
4. Remaining stable-presentation/configuration projections in state migration.
5. Real 30-minute Resting/Observing continuity and release/owner acceptance.

No commit, push, merge, branch switch, main modification, installer, approval
promotion, deletion of earlier reviews, or .asset-staging modification.
