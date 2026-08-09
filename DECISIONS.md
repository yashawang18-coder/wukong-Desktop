# Decisions

## 2026-08-05 — approve basic-action keyframes only

The owner approved A1–A2, B1–B2, C1–C4, D1–D4, E1–E3, and F1–F2 as visual keyframe anchors. This approval is recorded only as `owner_preview_approved=true` at the `approved-keyframes` stage.

Real renderer validation is still required before any runtime approval or application use. Until that validation passes, every action in this batch must retain `runtime_approved=false` and `runtime_use=false`.

Source PNGs remain lossless 1024×1024 RGBA files. No review GIF is included because this batch contains keyframes only; future GitHub review GIFs must be separately downscaled to 256 or 384 pixels and revalidated for frame count and duration.

## 2026-08-06 — separate behavior contracts from asset approval records

Preserve existing asset manifests as approval and provenance evidence. Add a separate pose vocabulary, stable behavior contracts, non-destructive asset sidecars, and runtime registry so replacing artwork does not change behavior identity or silently rewrite approval history.

Only owner-approved, real-renderer-verified `runtime-approved` assets may enter the runtime registry. Soft 720p video-derived movement remains motion-only evidence, and locally retained unpublished sources must be declared `local_unpublished` rather than treated as repository files.

## 2026-08-07 - import P0 generated action candidates without runtime approval

Import the 2026-08-06 generated P0 action evidence as a repository-scoped review batch while preserving existing approval and contract records.

The import keeps all 17 existing owner-approved keyframes byte-unchanged and does not overwrite or duplicate them. Existing `contracts/asset-sidecars/*.video_v2.json` files remain unchanged.

Imported scope:

- Standard stand-idle A2 reuse approval record.
- Sit/stand approved key poses and owner approval record.
- Sit/stand runtime-candidate intermediate frames and review records.
- Walk-start and walk-stop approved transition anchor records.
- Walk-start runtime-candidate intermediate frames.
- V4 geometry-stabilized walk-stop effective `stop-i2` frame and owner preview approval record.
- JSON manifests, README/REVIEW files, tests, and status documentation.

Excluded scope:

- Source ZIP files.
- `.tmp` files, `__pycache__`, and caches.
- `contact.jpg` and all contact/review contact images beyond the existing approved C1 keyframe.
- GIF and JPG previews.
- Unsuitable standing candidates under `standing/candidates/v1`.
- Rejected walk V4 `stop-i1`, `attempt2`, and related evidence images.
- The video-v2 base actions source tree, raw videos, video-v2 derived frames/GIFs/manifests, and video hash records.
- Private source identifiers.

Runtime boundary: `approved-keyframes` is not `runtime-approved`; new animations remain `runtime-candidate`, with `runtime_validation=pending`, `runtime_approved=false`, and `runtime_use=false`. Windows real desktop renderer validation remains required before runtime registration or application use.

## 2026-08-09 - use one behavior pipeline for all request sources

Owner UI actions, right-click commands, autonomous ticks, schedules, developer simulations, and model-proposed intents enter the same request and arbitration pipeline. No UI or model integration may call the animation orchestrator directly or mutate runtime state.

Accepted, rejected, and deferred decisions must be explainable. Execution reports `Started`, `Progressed`, `Completed`, `Interrupted`, or `Failed`; state and memory changes derive from recorded outcomes rather than unrestricted property writes.

## 2026-08-09 - treat Pupu as selective read-only engineering reference

A pinned complete Pupu source snapshot may be supplied to preserve dependency context, but it is not a codebase to rename or re-skin. Transparent Windows hosting, input capture, settings, diagnostics, packaging, and similar infrastructure may be selectively adapted after audit. Behavior selection, animation lifecycle, state mutation, model action triggering, memory flow, and manifest integration must follow Wukong contracts and should be rewritten where Pupu violates them.

Pupu assets, behavior IDs, hard-coded mappings, secrets, photographs, personal settings, memories, albums, and build outputs are prohibited from Wukong release artifacts.

## 2026-08-09 - keep normal UX and developer diagnostics in one product

The control panel uses six primary tabs: Owner, Profile, Album, Model, Assets, and Developer. The Owner and Model areas have independent conversation sessions. Normal mode presents user-facing state and feedback; developer mode adds trace, state-machine, behavior, agent, memory, scene simulation, log/test, and technical-description views. Simulation must be visibly isolated and restorable and must not write real memory.
