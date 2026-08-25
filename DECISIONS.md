# Decisions

## 2026-08-23 - derive daily candidates without widening source approvals

Approved light-malt-gold pixels may be copied into a new versioned batch for a proposed daily semantic, but approval does not transfer automatically from owner-command use to autonomous use. `WK-AUTONOMOUS-DAILY-BEHAVIORS-v1` therefore preserves source bytes and hashes while keeping all autonomous and runtime gates closed until owner semantic review and real Windows renderer QA pass.

No expired red/standard-Shiba pixel may be copied into the batch. Those assets remain motion/timing references only. The active autonomous pool remains the already approved P2 lifecycle set.

## 2026-08-23 - make manifest runtime gates authoritative for prone touch

`WK-INTERACTION-PRONE-TOUCH-v4-1` declares `runtime_validation=pending`, `runtime_approved=false`, `runtime_use=false`, and runtime registration forbidden. The desktop catalog must mirror those flags. A normal owner touch may update bounded social/comfort state, but it must return `Deferred` without requesting the candidate animation. Only developer-forced preview may play it before Windows validation and explicit approval.

## 2026-08-23 - separate interaction intent, state response, asset response, and dialogue

Touch, stroke, rapid tap, drag, and double-click first pass through a single interaction decision service. The service may record immediate bounded input effects, reject unsafe or unwanted contact, or choose a behavior only when pose and runtime gates permit it. Drag remains window movement, and double-click remains the explicit chat entry; neither invents a pet animation.

Initiative speech is a separate state-driven decision, not a timer that always speaks. It must be suppressed during non-idle behavior, petrification, expanded chat, quiet hours, high stress, low relationship acceptance, and cooldown. A displayed initiative line is transient pet expression: it must not be inserted into owner/assistant conversation history and must not invoke a background model.

## 2026-08-23 - use one runtime state and one autonomous scheduler

Desktop behavior state has one authority: `PetRuntimeState`. UI metrics are projections, not an independently mutated copy. A requested action may mark the state busy, but posture, relationship, and completed-action effects are committed only from the renderer completion callback. Interrupted or failed playback must not commit the planned end posture.

The Behavior Agent developer switch may expose diagnostics and deterministic previews, but it must not replace production autonomous scheduling with a second execution path. Autonomous selection uses the approved posture-compatible lifecycle pool until additional daily assets receive their own autonomous approval. Legacy red/standard assets and owner-command-only v4 actions remain outside that pool.

GitHub Actions validates pushes to `agent/**` in addition to `main`, pull requests, and manual dispatch so feature commits cannot silently bypass the Windows build.

## Portable user data and shareable defaults - 2026-08-22

Decision:

- Store editable desktop user data in `WukongData/` beside a writable portable executable, with a safe `%LOCALAPPDATA%/Wukong` fallback only when the executable directory cannot be used.
- Ship non-secret initial files from `config/defaults/` as `WukongDefaults/` and seed only missing user files.
- Keep albums, conversation history, memory candidates, logs, and credentials out of Git. A locally assembled package may include albums under `WukongData/albums/`, and conversation history can be removed before sharing.
- Keep API keys in Windows Credential Manager; neither migration nor package preparation may export them.
- Double-clicking the pet opens a compact single-row input directly below the current frame's Alpha-visible subject instead of opening the control panel. Replies appear above the visible subject in a separate speech bubble. Occasional initiative speech is local-template based and does not call a model without an explicit conversation request.

Reason:

The owner needs locally packaged Windows copies whose prompt and initial settings travel with the executable, while private albums and conversations remain controllable local files and never become repository content. A single portable data layout also removes the previous split between executable behavior and `%LOCALAPPDATA%` configuration.

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

## 2026-08-15 - promote P2 lifecycle microloops to autonomous runtime after Windows QA

The forward 14-state lively daily sequence, P0/P1 reverse transitions, and stand/sit/prone microloops passed owner Windows transparent-renderer QA on 2026-08-15 and may be used by the formal autonomous lifecycle profile.

Allowed scope:

- Index `WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2` through the existing Wukong asset catalog.
- Set `visual_approved=true`, `art_candidate=false`, `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, and `runtime_use=true`.
- Activate only these autonomous mappings: full lifecycle as low-frequency self-directed daily behavior, stand-idle for stable stand, sit-idle for stable sit, and prone-idle for stable prone.
- Keep developer diagnostics for phase/frame/duration/profile inspection and 128/192/256 sizing.

Forbidden scope:

- Do not use `wk.command.*` IDs.
- Do not expose these candidates in the normal right-click menu or production command routing.
- Do not add them to `contracts/runtime/asset-registry.json`, `pet_assets.json`, magic, coins, training data, or command behavior mappings.
- Do not edit the 32 manually accepted PNGs to hide the strict y=932 non-zero-alpha audit finding; document that Windows QA found no visible artifact instead.

## 2026-08-16 - accept car ride v8 as visually approved runtime candidate for prototype preview only (superseded)

Decision: accept `WK-INTERACTION-CAR-RIDE-CANDIDATE-v8` as a visually approved, runtime-pending candidate asset package and allow it to be exercised only through `PrototypePreview` in a candidate EXE for Windows human QA.

Superseded on 2026-08-16 by the runtime promotion decision below after owner Windows transparent WPF visual QA passed.

Reason: the source material and extracted QA previews have completed owner visual confirmation. Code build, contract validation, automated tests, controlled launch, and bounded-cache smoke have passed. Windows transparent WPF renderer human QA has not yet been completed.

Runtime restrictions:

- `Normal` continues to reject this candidate action.
- Dialogue and AutonomousTick must not use `PrototypePreview`.
- Do not add it to the autonomous behavior pool.
- Do not add it to model routing.
- Do not add it to command routing.
- Do not add it to the formal runtime registry.
- Keep `runtime_approved=false`.
- Keep `runtime_use=false`.
- Only after owner-completed Windows human visual QA and explicit runtime approval may these runtime states change.

Verification facts preserved with this decision:

- UX closure baseline: `d5136a0810592b1eb23c6eaf7da0f0c5ea26f2ac`.
- Car ride branch baseline: `d5136a0810592b1eb23c6eaf7da0f0c5ea26f2ac`.
- Original ZIP SHA-256: `bf92f38e3cc976236584d8581cbb8f0f1965257c31837c0d1fd69c7670e9f7e1`.
- Published directory PNG count: 253.
- Manifest runtime frame references: 222.
- Five-minute cache smoke: 299 samples; Private MB 93.5 -> peak 238.3 -> end 228.9; Working Set MB 154.8 -> peak 303.5 -> end 295.9; decoded cache peak 36 frames / 144 MB; evictions 1215.
- The cache smoke proves the runtime did not keep all 222 frames resident at once; it is not a substitute for real renderer QA or long-duration acceptance.
- Release build passed with 4 existing warnings.
- Contract validation passed with 0 errors and the existing 9 known gaps.
- Python tests passed 35/35.
- All C# console self-tests passed.
- Controlled launch and exact-PID termination passed.
- `MainWindowHandle=0` is not used as a transparent WPF visibility failure criterion.

## Decision: Promote WK-INTERACTION-CAR-RIDE-CANDIDATE-v8 to approved manual owner runtime

Date: 2026-08-16

Decision:
The owner completed Windows transparent WPF runtime visual QA for the v8 candidate EXE and approved `wk.interaction.car_ride` for the normal manual owner interaction path: `玩一下 > 兜风`.

Runtime state:

- `visual_approved=true`
- `runtime_validation=passed_windows_renderer_qa`
- `runtime_approved=true`
- `runtime_use=true`
- `prototype_use=false`

Reason:
The source package and automated validation had already passed, and the owner has now confirmed real desktop renderer behavior for the candidate EXE, including the locked menu labels and Apparate regression fixes.

Scope restrictions:

- Only explicit owner UI selection of `玩一下 > 兜风` may trigger the action through the normal approved runtime gate.
- Do not add v8 to `AutonomousTick`.
- Do not add v8 to dialogue/model routing.
- Do not add v8 to command/口令 routing.
- Do not use v8 for `吃一下`, `散步`, startup auto-play, or any locked feature.
- Keep PrototypePreview infrastructure for other candidate assets; v8 no longer depends on it.
- No installer or public release is approved by this decision.

## Decision: Approve WK-COMMAND-PRODUCTION-CANDIDATES-v4 for manual owner commands

Date: 2026-08-19

Decision:
Promote `WK-COMMAND-PRODUCTION-CANDIDATES-v4` from command mock/prototype preview to approved manual owner command runtime material.

Runtime state:

- `motion_design_approved=true`
- `production_asset=true`
- `visual_approved=true`
- `runtime_approved=true`
- `runtime_use=true`
- `prototype_use=false`
- `asset_stage=runtime_approved_owner_command`

Reason:
The owner provided the corrected real Wukong command assets and requested that they replace the rough command mock path, appear in the control panel command material section, and run from the right-click command menu.

Scope restrictions:

- Only explicit owner command paths may trigger the batch: desktop context menu and control panel command asset page.
- Paw and Eat must branch by current stable posture instead of using a single generic action.
- Do not add the batch to `AutonomousTick`.
- Do not add the batch to dialogue/model routing.
- Do not use the batch for startup autoplay or unrelated interactions.
- Keep `WK-COMMAND-ACTION-CANDIDATES-v3` as expired motion reference material; do not delete it, runtime-enable it, or route owner commands to it.

## 2026-08-21 - preserve terminal posture and use only posture-compatible low-frequency autonomous loops

Owner-command completion must briefly settle on the exact terminal frame from the executed motion, with the same visual scale and asset provenance, then enter the approved microloop matching the declared end posture. It must not substitute an unrelated idle frame, freeze indefinitely, or normalize every command back to stand.

The hold must preserve the computed render scale of the complete motion that actually ran. Recomputing alpha-bound normalization from the terminal frame alone is forbidden because it changes visible size at the lifecycle boundary.

Autonomous basic playback may select only a sequence compatible with the current stable posture. Startup posture is derived from energy, stress, and arousal; it is not hard-coded to prone. The approved P2 stand, sit, and prone microloops and the full lifecycle sequence remain the active source set. Minimum dwell, state gates, cooldown, repetition suppression, and randomized scheduling are required to prevent permanent holds and rapid state switching. A normally completed full lifecycle exits to stable stand before the stand microloop begins.

Legacy red/standard basic visuals and the old prone silent-breathing presentation are hidden from active runtime selection and labeled `已过期`, but are not deleted or rewritten. They remain motion references. In particular, `WK-CORE-PRONE-IDLE-LF-v1` remains pending replacement comparison because its breathing and blink design may still be useful.

## 2026-08-21 - correct approved-source defects through versioned derivatives

An approved source frame must not be edited in place to repair a localized defect. Preserve the original, create a versioned runtime derivative, record the correction and hashes, and keep unaffected frames byte-identical. The v4 Down blue-background correction follows this rule through `frames/down-v2`.

Candidate coin-edge cleanup may preserve the existing alpha mask, dimensions, and visible bounds while correcting contaminated boundary RGB. Such a correction does not promote the magic batch or change its runtime gate.

## 2026-08-21 - separate effect-specific scale, pacing, and movement policy

Coin scale and stone-dog scale are independent visual policies. The accepted coin remains at two-thirds of normal visible pet height; petrify and release use the same 0.92 presentation scale as the approved P2 basic and v4 command pet motions, while retaining slower transition timing.

When one behavior contains visually distinct phases, phase-level scale is part of the animation lifecycle. Petrification applies scale 0.92 to the stone-dog `intro` and scale 2/3 to the initial coin `loop` before each phase's first frame is displayed; a later coin state request must not cause a second size correction.

Accio Broom and Car Ride owner showcases run for a randomized 10-20 seconds. Car Ride movement must use work-area-safe acceleration, sustained straight travel, adjacent direction-ring turn assets, and braking. Runtime interpolation, frame rewriting, or parallel effect players are not permitted.

## 2026-08-24 - freeze a complete coin outline and reference shared autonomous frames

The owner reported visible white edging and missing portions of the coin contour and authorized a full candidate-baseline repair. This supersedes the earlier limited instruction to preserve the defective coin alpha masks. All eight faces must instead share one complete anti-aliased outline, and all four flip states must share the same geometry at each flip step. The repaired PNG bytes and SHA-256 inventory form a frozen candidate baseline; this does not constitute owner visual approval or open any magic runtime gate.

Autonomous daily semantics must reference approved source motion ranges rather than copy their PNGs. A binding records source batch, behavior, phase, one-based range, and concatenated source-byte SHA-256. Candidate semantic gates remain independent of source visual approval: approved command or lifecycle pixels do not automatically authorize spontaneous playback. Resolution must fail closed if source identity, status, range, or bytes no longer match.

## 2026-08-26 - review V3R1 side-prone lifecycle and V4 forward-prone sequences in parallel

Decision:

Import V3R1 recovered lifecycle and V4 forward-prone as two separate developer-only owner-QA profiles. V3R1 preserves its historical side-prone identity across intro, idle, and exit. V4 is reviewed independently as a forward-prone calm loop and a one-shot lick microevent.

Reason:

The two packages have different terminal prone orientations and no approved side-prone to forward-prone bridge. A hard cut would misrepresent lifecycle continuity even though both source packages pass offline checksum and PNG integrity checks.

Restrictions:

- Do not replace or edit the approved V2 lifecycle binding or PNG files.
- Do not concatenate a V3R1 side-prone phase with a V4 forward-prone phase.
- Keep both new profiles out of Normal, owner UI, autonomous scheduling, dialogue, model, command, magic, and car ride routes.
- Permit playback only through the existing isolated `DeveloperPreview` BehaviorRequest path.
- Owner visual QA passed on 2026-08-26 for the seven V3R1/V4 entries actually reviewed. Record `visual_approved=true` and `runtime_validation=owner_visual_qa_passed_runtime_behavior_pending`, while keeping `runtime_approved=false`, `runtime_use=false`, `production_asset=false`, and autonomous bindings disabled until runtime behavior is revalidated.
- The owner-approved V4 stable anchor is anchor-only evidence and cannot promote the derived loop or lick sequence.
- Preserve the original package manifests and SHA inventories unchanged; Wukong-specific review mapping lives in separate `asset.json` and `runtime-review-manifest.json` files.

## 2026-08-26 - retire prone touch and separate autonomous daily behavior from commands

Decision:

Retire the panel asset named `摸摸回应`, identified from source as `wk.interaction.prone_touch` / `wk.interaction.prone_touch.v4.1`, and make autonomous daily selection an explicit semantic allowlist.

Reason:

The owner rejected the touch response and confirmed that ordinary daily behavior must not spontaneously jump or spin. Developer-preview capability is not evidence that an asset is enabled or eligible for autonomous use.

Restrictions:

- Preserve the touch PNGs and historical metadata, but mark the batch deprecated and reject it before every execution-mode bypass, including DeveloperPreview and fallback.
- Hide deprecated assets from default material views; expose them only under the expired-only filter.
- Permit autonomous selection only for approved stable posture idles, breathing, blink, minor observation/adjustment, and separately approved low-frequency daily lifecycle behavior.
- Keep jump and spin available to explicit owner commands and eligible developer preview, but never derive them through tags, fallback, shared frames, or random autonomous sampling.
- Keep V3R1 and forward-prone V4 independent until an approved bridge exists; visual approval does not permit a hard splice or runtime activation.
