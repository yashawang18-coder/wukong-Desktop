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

## 2026-08-12 - integrate command action candidates behind runtime gates

Import `WK-COMMAND-ACTION-CANDIDATES-v3` from the remote candidate asset branch as runtime-candidate evidence only. The four command behaviors use explicit stable IDs instead of folder-name discovery in production routing:

- `wk.command.paw_rise`
- `wk.command.jump`
- `wk.command.spin_approach_stop_sit`
- `wk.command.paw_eat`

These sequences may appear in the asset library and may be force-played in developer mode to collect Windows renderer evidence. They must remain excluded from the autonomous pool and production runtime registry until renderer QA promotes the assets to `runtime-approved` with `runtime_use=true`.

Manual transparent-window validation on 2026-08-12 failed all four command candidates for `color_inconsistency`, `geometry_scale_jitter`, and `uneven_timing`. Keep the integration and developer preview path, but do not approve these assets, do not open production command execution, and do not add them to autonomous behavior selection until a corrected batch passes renderer QA.

## 2026-08-13 - allow owner-only magic mock prototype previews without production approval

Add a Wukong-only magic mock batch for explicit owner-triggered prototype playback while preserving the production runtime gate.

Allowed scope:

- Right-click `宠物魔法` menu and control-panel `魔法特辑` may submit `BehaviorRequest`-equivalent desktop requests with `source=OwnerContextMenu` or `source=ControlPanel` and `executionMode=PrototypePreview`.
- Only the explicit magic whitelist may use this path: `wk.magic.accio_broom`, `wk.magic.apparate`, `wk.magic.petrificus_totalus`, `wk.magic.petrificus_release`, and `wk.magic.scourgify`.
- The manifest must keep `prototype_use=true`, `runtime_approved=false`, `runtime_use=false`, and `production_asset=false`.

Forbidden scope:

- Do not add mock magic assets to the production runtime registry or autonomous behavior pool.
- Do not let Dialogue, model output, memory, personality, or autonomous tick set `PrototypePreview`.
- Do not treat mock playback as renderer QA or formal asset approval.
- Do not copy Pupu code, assets, behavior IDs, or mappings.

## 2026-08-15 - integrate reviewed magic candidates and interactive petrification coin behind the prototype gate

Replace the active magic playback source with `WK-MAGIC-SPECIALS-CANDIDATE-v1` while retaining the former mock batch as non-active historical evidence. The new batch packages reviewed V8 broom, invisibility, petrification, and restore frames; Scourgify remains explicitly identified as mock artwork until a reviewed replacement exists.

The broom motion may select any of eight directional frame loops from the velocity vector and must retain the loop phase across direction changes. The existing ordered-PNG player remains the frame consumer; this change does not introduce a sprite-atlas runtime dependency.

After the petrification transition, the runtime enters an owner-interactive coin state machine:

- `vivid/front` is the initial state; it settles to `flat` after 800 ms.
- Inactivity changes the current face to `faded` after 10 minutes and `exhausted` after 20 minutes by default. These thresholds are configurable without changing asset identity.
- A single click resolves to `vivid/front` and resets inactivity.
- A front double-click flips to the same-state back and preserves inactivity.
- A back double-click flips to `vivid/front` and resets inactivity.
- Single-click dispatch is deferred until the double-click window expires.

Coin interaction is valid only while petrification was entered through an owner `PrototypePreview`. Dialogue, autonomous, model, and memory sources remain forbidden. All candidate and coin manifests retain `runtime_validation=pending`, `runtime_approved=false`, `runtime_use=false`, and `production_asset=false`; the runtime registry remains empty until a Windows transparent-renderer run is reviewed.
