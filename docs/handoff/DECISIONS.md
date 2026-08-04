# Decisions

## Confirmed

| Date | Decision | Consequence |
|---|---|---|
| 2026-08-04 | `wukong-current-adult-v1` uses the owner-approved candidate 05 identity reference. | Future actions must cite the exact external asset version before use. |
| 2026-08-04 | Neutral geometry and tongue-out smile are separate anchors. | A smile must not leak into neutral actions. |
| 2026-08-04 | Identity approval and animation approval are separate. | No action becomes runtime-ready merely because the dog looks correct. |
| 2026-08-04 | Real photographs are not uploaded to the public repository. | Photos may be provided privately as temporary evidence only. |
| 2026-08-04 | Current assets are managed in another project and are excluded from this documentation change. | Later work must obtain its exact URL/ref/SHA instead of assuming availability here. |
| 2026-08-04 | Repository facts and executable validation outrank chat summaries. | Every handoff begins with a read-only repository and CI audit. |
| 2026-08-05 | The five `WK-CORE-PRONE-IDLE-LF-v1` source poses are approved keyframes. | They may be used to assemble runtime candidates but are not automatically runtime-approved. |
| 2026-08-05 | The first loop uses 12 frames at 8 FPS and 1500 ms, with deterministic premultiplied-alpha interpolation between approved neighbors. | Approved anchors remain byte-exact; the duplicate closing source frame is excluded from playback to prevent a stall. |
| 2026-08-05 | The assembled first loop remains `runtime-candidate` with `runtime_use=false`. | Owner preview review and actual desktop renderer QA are still required before runtime registration. |

## Pending decisions

- Source-code repository and baseline version.
- Desktop framework and supported Windows versions/architectures.
- External asset project location and approved commit.
- Runtime renderer, atlas/individual-frame format, scale/pivot conventions, and performance budget.
- Installer, signing, update channel, rollback, and release retention policy.
- Exact behavior catalog and implementation priority.

Add a dated row whenever the owner approves a design boundary, asset state transition, architecture change, or release policy. Never rewrite history silently; supersede an older decision explicitly.
