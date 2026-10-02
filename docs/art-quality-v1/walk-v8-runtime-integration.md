# Walk v8 Runtime Integration

## Assets and direction

Canonical directory: `assets/action-batches/WK-AUTONOMOUS-PATROL-WALK-v8`.
Thirteen PNGs are copied without reencoding from the owner-accepted normalized
review. The existing IDs `wk.candidate.autonomous.patrol_walk_left_v1` and
`wk.candidate.autonomous.patrol_walk_right_v1` resolve to this batch, not v1.
The version in a historical behavior ID does not select an old art directory.
Right is a single horizontal WPF render transform. Both variants share exactly
the same frame paths, timings, fixed scale (0.68) and anchor (512,900).

| Phase | Frames | Duration |
| --- | --- | --- |
| Intro | stand, shift, reach, cycle 1 | 500/180/180/200 ms |
| Loop | cycle 1-8 | 200 ms each |
| Exit | cycle 1, stop-step, stop-settle, stand | 200/240/300/700 ms |

Autonomous walks choose 2-4 complete cycles. They inherit the existing posture,
episode, busy, memory and approval gates. Both decision engines filter out blocked
directions, prefer continuation in the current direction, and share a 45-second
cooldown. No timer directly starts a PNG outside BehaviorRequest.

`PatrolWalkPlayback.cs` extends the existing MainWindow executor. One clock drives
frames and window movement, respecting original frame duration. The stand at each
end does not slide. The existing bounded bitmap cache and source decoder are reused.
The normal animation timer is suspended during walking and resumed afterwards.
Normal menu Stop finishes the current gait loop and plays exit once. Repeated Stop
does not create another exit task. High-priority replacement/close cancels safely.

## Mirror audit

The existing whitelist now covers 28 nonexpired catalog entries: P2 (4), V3R1 (5),
V4 prone idle/lick (2), prone head microevent (1), daily transitions (4), commands
(8), food/water (2) and v8 walking (2). Existing gates still apply independently;
mirror eligibility never enables an otherwise disabled asset. Stable commands and
idle inherit facing; they do not randomly flip on each tick or animation frame.

Magic/coins, native car directions, front-only expression/panting/expectant groups,
and deprecated entries retain their explicit restrictions. The old paired patrol
PNGs stay in source history, outside the active loader and publish output.

## Validation and delivery

- PNG/source byte identity, dimensions, RGBA, strict alpha borders, zero transparent
  RGB, sequence order/duration, shared direction paths and copy hashes are tested.
- One hundred seeded Exploring decisions check Normal mode, direction availability,
  2-4 loops, ending Stand, reducer settlement and inherited facing.
- Real WPF smoke: two full runs and one repeated-stop run, with frozen images,
  bounded directed translation, all 13 frames and all three phases observed.
- Evidence: `.publish-check/walk-v8-renderer-check-final/renderer.json`,
  `mirror-audit.json` and left/right/stop PNG captures. These are technical rendered
  evidence, not a claim that the owner has reviewed the final desktop transitions.
- Candidate EXE: `.publish-check/walk-v8-runtime/Wukong.Desktop.exe`.
  Existing executables are not overwritten. Inspect basic actions' left/right walk
  cards, or let the existing Exploring episode choose walking naturally.

Owner still needs to judge perceived gait/material continuity, start/stop joins,
and different-size/DPI desktop presentation. No new turning animation was invented:
direction changes happen only between requests, never halfway through a gait cycle.

## Executed validation (2026-10-02)

- Release solution build: passed, six existing warnings; self-contained win-x64
  folder publish: passed. The shorter `walk-v8-runtime` directory avoids paths
  reaching the legacy Windows 260-character limit in the initial long name.
- C# console suites: Domain 5/5, Contracts 5/5, Application 58/58,
  Infrastructure 23/23, Desktop 121/121 (212 total).
- Python discovery: 91/91. Contract validator: zero errors, nine existing gaps.
- Final WPF evidence additionally includes a real Normal/AutonomousTick walk:
  13 unique rendered frames, intro/loop/exit, 8.958 seconds, rightward translation,
  recovery to standing, preserved facing and resumed idle animation.
- Publish comparison: 1,658 resource files / 1,449 PNGs match source bytes,
  including all 13 v8 PNGs. No old patrol-v1 folder, staging input or Pupu reference.
- Published EXE controlled launch: PID 16400 stayed alive for ten seconds. Its
  executable path was verified before termination; CloseMainWindow returned false
  (transparent tool window), so only that PID was terminated. Process exit verified.
- EXE launcher SHA256:
  `6a2ff785020dfd21cc537d2a47a9b89b5c445a96283ca2d41d49379d18b2e15d`.
  This is a folder publish: the native launcher hash may match an older version;
  the updated `Wukong.Desktop.dll` SHA256 is
  `fb8f6ed5714a18cd2247f0579f6a031db605f33c35c7f968088718394ab6fab2`.
- `git diff --check`: passed. No commit, push, merge or branch change.
