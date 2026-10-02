# Prone activation and direction/sleep review

## Runtime scope

Enabled: prone-satisfied-smile, prone-curious-observe, prone-knowing-look,
prone_happy_hot_panting. Three 12-frame 3030ms events and one 17-frame 5680ms
event retain source hashes, frame order, no-mirror policy and front-prone anchor.
The loader accepts either consistent pending-review gates or consistent approved
gates; an approval bit alone cannot open an inconsistent manifest.

Both selectors and the final autonomous submit gate apply shared cooldown,
front-pose, busy and energy/stress restrictions. Recently played expressions
cannot repeat until two other events displace them. The existing Reducer handles
completion once and restores compatible front idle. Previews do not learn.

Side-prone full-lifecycle preference multiplier is 0.20; side-idle multiplier is
0.45, on top of existing user preferences. Complete daily lifecycles loop 1-2
times. This lowers backward-looking exposure without faking a camera bridge.
Existing P2 and V3R1 exits already start at their former second frame; daily
prone-to-sit shares that trimmed range. Intro/loop source pixels are retained.
The full front-prone rise/entry connection remains an asset gap. A front-only
pet may keep front idle until a compatible owner action is available.

## New art is not runtime-ready

Review: `.publish-check/prone-walk-sleep-art/review.html`.
Raw files, hashes and machine-readable reasons: `candidate.json`, `SHA256SUMS.txt`.
Built-in ImageGen was used with approved walk-v8 identity and the accepted
front-sleep v3 / awake-front V4 references. Prompts requested coherent full bodies,
malt-gold fine fur, stable paws, four-beat walking and tiny breathing deltas.
No existing PNG was edited, no head was composited and no frame was interpolated.

- Front-left and rear-left walking: one candidate phase per angle, not cycles.
- New front-sleep exhale: independent material candidate.
- Sleep inhale: rejected because head/muzzle displacement exceeds the tiny breath.
- Initial 8-cell walking sheet: rejected for repetitive poses/background quality.
- Single frames are 1254x1254 raw RGBA; strict border noise remains in three.

Remaining work: approve angle identity, generate validated full gait + start/stop
per angle, fix breathing with a stable head, add actual entry/wake/turn bridges,
then normalize complete groups consistently and run renderer QA. These outputs
are not packaged as runtime sources and all new-art approval flags remain false.

## Validation locations

Actual WPF four-action evidence: `.publish-check/prone-check/pre-approval/`.
New regression tests cover Normal gate, duplicate completion, compatible recovery,
pose exclusion, shared cooldown, reduced side dwell and retained exit trimming.
Delivery: `.publish-check/prone-life-v1/Wukong.Desktop.exe` (folder publish).
Final owner desktop QA: front-event subtlety, return continuity, side-pose frequency.
No Git commit, push, merge, branch switch, old-version cleanup or main update.

## Final verification

- `dotnet build Wukong.sln --configuration Release --no-restore`: passed, 6 existing warnings, 0 errors.
- All console suites: Domain 5/5, Contracts 5/5, Application 58/58, Infrastructure 23/23, Desktop 123/123 (214 total).
- `python -B -m unittest discover -s tests -v`: 91/91 passed.
- `python -B tools/validate_contracts.py`: 0 errors, 9 existing lifecycle gaps.
- `git diff --check`: passed (existing CRLF conversion notices only).
- Real transparent WPF playback: each of the four actions exercised via Normal and DeveloperPreview, 106 frame-path observations total, recovery `prone.awake.front`, no busy lock. Evidence: `.publish-check/prone-check/final-renderer/renderer.json` and frame screenshots. This is actual rendered playback, not the 30-minute virtual-clock unit test.
- Self-contained win-x64 folder publish passed. `.publish-check/prone-check/publish-validation.json` verifies all 1660 output assets / 1449 PNGs against source hashes; 53 newly enabled prone PNGs unchanged. New generated art and staging files are absent from runtime paths.
- EXE SHA256: `6a2ff785020dfd21cc537d2a47a9b89b5c445a96283ca2d41d49379d18b2e15d`.
- Desktop DLL SHA256: `e352a70df5a7996148c841a27bc5fdb252de881c95e784e73207a10f6dd28b9a`.
- Independent published EXE survival check is blocked: the owner is running `walk-v8-runtime` (PID 66992); the new EXE exits with code 0 due to the existing single-instance contract. No owner process was terminated. Close the old app before testing the new folder.

The branch remains `codex/decision-memory-speech-command-v2`, HEAD
`cc32bb7093707df7c09223d869c842d9cd85be1d`. The worktree includes earlier user
changes, including previously authorized side-composite removal; this turn did
not perform those deletions or modify `.asset-staging/`.
