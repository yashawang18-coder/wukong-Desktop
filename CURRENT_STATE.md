# Current state

## WK-BASIC-ACTIONS-BATCH-v2

- 17 keyframes across six action packages are owner-preview approved.
- Stage: `approved-keyframes`.
- `owner_preview_approved=true`.
- `runtime_validation=pending`.
- `runtime_approved=false` and `runtime_use=false`.
- No interpolation, runtime-frame generation, application registration, or real-renderer playback has been performed for this batch.
- Corrected anchors in the approved set: C3 uses the reviewed 110% scale correction; F2 uses the reviewed F1-aligned coat color.

The existing prone-idle V3 animation remains a separate `runtime-candidate` and is not promoted by this batch.

## WK-P0-GENERATED-ACTIONS-2026-08-06

- Imported standard stand-idle A2 reuse approval, sit/stand approved key poses, sit/stand runtime-candidate intermediate frames, walk-start/stop approved transition anchors, walk-start runtime-candidate frames, and the V4 geometry-stabilized walk-stop effective `stop-i2` frame.
- Existing 17 approved keyframes remain byte-unchanged and are not duplicated in the new batch.
- Existing `contracts/asset-sidecars/*.video_v2.json` files are preserved and unchanged.
- `approved-keyframes` remains visual key-pose approval only; it is not `runtime-approved`.
- New sit/stand and walk transition animation records remain `runtime-candidate`.
- `runtime_validation=pending`.
- `runtime_approved=false`.
- `runtime_use=false`.
- Windows real desktop renderer validation is still required for playback, scale, pivot, alpha edges, direction, memory, interruption, and runtime registration.
- Excluded from the import: source ZIPs, `.tmp` files, caches, contact review images, GIF/JPG previews, unsuitable standing candidates, rejected walk-stop `stop-i1`/`attempt2` evidence, raw videos, video-v2 derived files/manifests, video hash records, and private source identifiers.

## P0 contract foundation

- A non-destructive contract foundation is present under `contracts/`.
- It defines four canonical stable poses, six version-independent behavior contracts, six legacy/candidate asset sidecars, JSON Schemas, an intentionally empty runtime registry, and generated pose-graph/P0-gap artifacts.
- The existing `asset.json` files, hashes, approval states, and runtime flags are unchanged.
- Four soft 720p-derived movement candidates are recorded as `motion_only` and `local_unpublished`; they cannot be runtime-approved or registered.
- The prone-idle V3 sidecar points to its repository manifest but remains `runtime-candidate`, `runtime_use=false`, with desktop renderer QA pending.
- Contract validation reports `0 error(s)` and nine explicit lifecycle gaps.
- Six focused contract-foundation unit tests pass locally.
- The runtime asset registry has zero bindings. No application integration or real-renderer validation is claimed.
