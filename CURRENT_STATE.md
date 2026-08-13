# Current state

Last verified: 2026-08-09 (Asia/Singapore), `main` at `2f7e949c21bd88d2a4cc49977778f4c517dd962a`.

## Repository summary

- PR #2, PR #3, and PR #4 have been merged into `main`; PR #1 is the remaining documentation PR being refreshed against the consolidated repository.
- The repository contains Wukong identity and P0 asset evidence, behavior/pose contracts, schemas, validators, generated gap reports, and Python tests.
- It does not yet contain the Windows desktop application, WPF host, backend/behavior runtime, model provider implementation, installer, or Pupu reference source.
- The reviewed Wukong six-tab UX exists as a product-design input but is not currently committed in this repository.
- `python3 -m unittest discover -s tests -v` passes 25 tests in the verified checkout.
- `contracts/runtime/asset-registry.json` has zero runtime bindings. No asset is runtime-approved or enabled for application use.

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

## WK-INTERACTION-PRONE-TOUCH-v4-1

- PR #4 is merged into `main` as a 70-frame additive runtime candidate with `intro`, `loop`, `exit`, and `interrupt_exit` sequences.
- Owner preview approval is recorded, but `runtime_validation=pending`, `runtime_approved=false`, and `runtime_use=false` remain unchanged.
- It is evidence for the future animation lifecycle implementation; it is not yet a production behavior binding.

## WK-COMMAND-ACTION-CANDIDATES-v3

- Fetched from `origin/agent/assets-command-actions-v3-candidate` commit `fc9f0fd`.
- Imported four command-action candidate sequences under `assets/action-batches/WK-COMMAND-ACTION-CANDIDATES-v3/`:
  - `01_sit_prone_paw_rise`: 8 PNG frames.
  - `02_jump`: 8 PNG frames.
  - `03_spin_approach_stop_sit`: 10 PNG frames.
  - `04_sit_prone_paw_eat`: 9 PNG frames.
- Added a batch manifest with frame order, timing, pose, interruption policy, dimensions, byte sizes, and SHA-256 hashes.
- Added candidate behavior contracts for `wk.command.paw_rise`, `wk.command.jump`, `wk.command.spin_approach_stop_sit`, and `wk.command.paw_eat`.
- These assets are visible in the desktop asset library and may be force-played from developer mode for Windows renderer validation.
- Manual transparent-window validation failed for all four sequences with `color_inconsistency`, `geometry_scale_jitter`, and `uneven_timing`.
- They are not added to `contracts/runtime/asset-registry.json`, not included in the autonomous behavior pool, and remain `runtime_validation=failed`, `runtime_approved=false`, `runtime_use=false`.
- Production command routing may resolve to these stable behavior IDs, but the runtime gate must return `Deferred` until corrected assets pass explicit renderer QA and runtime approval is recorded.
- The extra seven PNG files in the batch are preview/reference images only: four contact sheets, `all-groups-overview.png`, `shared-prone-proof.png`, and `shared-sit-proof.png`. They are outside every action `frames[]` list and are not registered as playable frames.

## WK-MAGIC-SPECIALS-MOCK-v1

- Added local Wukong-only mock prototype action batch under `assets/action-batches/WK-MAGIC-SPECIALS-MOCK-v1/`.
- The batch contains transparent PNG frame sequences and manifest records for:
  - `wk.magic.accio_broom`
  - `wk.magic.apparate`
  - `wk.magic.petrificus_totalus`
  - `wk.magic.petrificus_release`
  - `wk.magic.scourgify`
- These assets are code-drawn mock placeholders for interaction/runtime validation only. They are not production artwork, not Pupu assets, and not final Wukong magic art.
- Batch state remains `runtime_validation=pending`, `runtime_approved=false`, `runtime_use=false`, `production_asset=false`, with `prototype_use=true`.
- Prototype playback is allowed only through explicit owner entry points (`OwnerContextMenu` and `ControlPanel`) using `BehaviorExecutionMode.PrototypePreview` and the magic behavior whitelist.
- Dialogue, model output, autonomous tick, memory, personality, and normal production behavior requests must not use `PrototypePreview`; they continue to resolve through the normal runtime gate and defer until assets are formally approved.
- The batch is not added to `contracts/runtime/asset-registry.json`, not included in the autonomous pool, and not eligible for production command or model-triggered execution.

## Next implementation target

Phase 1 is the smallest honest end-to-end runtime:

`InputEvent -> Intent -> BehaviorRequest -> Eligibility -> Arbitration -> BehaviorExecution -> AnimationLifecycle -> Outcome -> RuntimeState -> Event/Memory -> Trace`

Before implementation, add or provide the reviewed UX artifact and a pinned Pupu source snapshot as read-only reference, then audit the actual source layout. Phase 1 must not expand into P1-P4 assets, complex long-term learning, cloud sync, macOS, or automatic frame generation.
