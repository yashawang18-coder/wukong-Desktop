# Decisions

## 2026-08-05 — approve basic-action keyframes only

The owner approved A1–A2, B1–B2, C1–C4, D1–D4, E1–E3, and F1–F2 as visual keyframe anchors. This approval is recorded only as `owner_preview_approved=true` at the `approved-keyframes` stage.

Real renderer validation is still required before any runtime approval or application use. Until that validation passes, every action in this batch must retain `runtime_approved=false` and `runtime_use=false`.

Source PNGs remain lossless 1024×1024 RGBA files. No review GIF is included because this batch contains keyframes only; future GitHub review GIFs must be separately downscaled to 256 or 384 pixels and revalidated for frame count and duration.

## 2026-08-06 — separate behavior contracts from asset approval records

Preserve existing asset manifests as approval and provenance evidence. Add a separate pose vocabulary, stable behavior contracts, non-destructive asset sidecars, and runtime registry so replacing artwork does not change behavior identity or silently rewrite approval history.

Only owner-approved, real-renderer-verified `runtime-approved` assets may enter the runtime registry. Soft 720p video-derived movement remains motion-only evidence, and locally retained unpublished sources must be declared `local_unpublished` rather than treated as repository files.
