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

## Pending decisions

- Source-code repository and baseline version.
- Desktop framework and supported Windows versions/architectures.
- External asset project location and approved commit.
- Runtime renderer, atlas/individual-frame format, scale/pivot conventions, and performance budget.
- Installer, signing, update channel, rollback, and release retention policy.
- Exact behavior catalog and implementation priority.

Add a dated row whenever the owner approves a design boundary, asset state transition, architecture change, or release policy. Never rewrite history silently; supersede an older decision explicitly.

