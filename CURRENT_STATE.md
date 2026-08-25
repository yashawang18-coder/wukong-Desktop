## Autonomous daily candidate and interaction decisions - 2026-08-23

- Added `WK-AUTONOMOUS-DAILY-BEHAVIORS-v1` with six proposed `wk.daily.*` actions and 59 frame references: stand-to-sit 10, sit-to-prone 12, prone-to-sit 4, sit-to-stand 5, playful hop 12, and playful spin 16. Every PNG is copied byte-for-byte from the approved light-malt-gold v4 command or P2 lifecycle batches with per-frame provenance and SHA-256.
- The derived daily batch is not runtime-enabled. Reusing an owner-command motion as spontaneous behavior changes its meaning, so `autonomous_semantics_owner_approved=false`, `runtime_validation=pending_owner_semantic_and_windows_renderer_qa`, `runtime_approved=false`, and `runtime_use=false` remain closed.
- Corrected the prone-touch catalog mismatch. `WK-INTERACTION-PRONE-TOUCH-v4-1` remains a 70-frame owner-preview candidate with `runtime_use=false`; normal touch now records bounded interaction state but returns `Deferred` and never requests its animation. Developer-forced preview remains available for renderer QA.
- Added one interaction decision service for touch, stroke, repeated tap, drag, and UI-owned double-click. It evaluates stable pose, interruptibility, petrification, stress, temperament sensitivity, relationship touch acceptance, and runtime asset availability before returning an animation ID. Rapid taps increase stress/arousal even when their response asset is locked.
- Replaced unconditional periodic speech with a state-driven initiative decision. Hunger, social need, boredom/play, curiosity, energy/rest, temperament, relationship initiative acceptance, cooldown, quiet hours, stress, current behavior, petrification, and expanded chat all participate. Initiative text is local and appears only in the transient speech bubble; it does not enter conversation history and does not call a model.
- The Linux checkout has no .NET SDK, so C# build execution is deferred to the branch's Windows CI. Python asset/provenance tests pass locally; no Windows renderer approval is inferred from those tests.

## Unified runtime state and autonomous arbitration - 2026-08-23

- `PetRuntimeState` is now the single desktop source for energy, hunger, social need, boredom, stress, mood, arousal, curiosity, comfort, focus, posture, busy state, and active action. Desktop metric properties are read-only projections of this state.
- Owner-command decisions remain pending while their animation plays. `ApplyOutcome(completed=true)` runs only after the renderer reaches `DesktopRuntimeHost.CompleteMotion`; owner stop records an interrupted outcome and does not commit the planned end posture.
- The developer Behavior Agent switch no longer replaces the formal autonomous scheduler. Every autonomous tick uses one posture-compatible lifecycle selector, one state, and one cooldown/history table. Temperament, boredom, curiosity, energy, stress, mood, arousal, comfort, dwell, cooldown, and repetition influence selection.
- Autonomous lifecycle completion updates posture and bounded state deltas without pretending that self-directed activity was an owner interaction. Trust and familiarity changes remain limited to completed owner/dialogue decisions.
- Windows CI now runs on `main`, every `agent/**` push, pull requests, and manual dispatch. A successful Windows run is still required before this change is called build-verified.
- Asset approval scopes are unchanged: legacy red/standard assets remain expired motion references, approved v4 actions remain owner-command-only, and approved P2 lifecycle assets remain the autonomous visual pool.

## Portable profile, album, and conversation data - 2026-08-22

- Version-controlled, non-secret initial settings live under `config/defaults/` and are published as `WukongDefaults/` beside the executable.
- On first run, missing defaults are copied into the writable `WukongData/` directory beside the executable. Existing `%LOCALAPPDATA%/Wukong` profile and conversation files are copied only when the corresponding portable file is absent.
- Pet prompt, pet/owner profile, model endpoint metadata, memory switches, pet scale, conversation history, and album-root preference now use the resolved portable data layout.
- API keys remain exclusively in Windows Credential Manager and are never copied into portable files.
- Portable albums belong under `WukongData/albums/`. User albums, conversation history, memory candidates, and credentials are not committed to Git.
- Clearing the final conversation session deletes `WukongData/agent/conversation-history.json`; the control panel also exposes an explicit clear-all action for preparing a blank shareable package.
- Double-clicking the visible pet opens a single-row input directly below the current frame's Alpha-visible subject. Replies and low-frequency initiative speech appear in a separate bubble directly above the visible subject; initiative speech uses local templates and never invokes a model in the background.
- Album media unlink and record deletion persist through the same Markdown media list without deleting local originals. Child-album removal is persisted separately and removes only the album from Wukong's catalog.

## WK-INTERACTION-CAR-RIDE-CANDIDATE-v8 runtime promotion - 2026-08-16

Windows owner visual QA passed on 2026-08-16 using the local transparent WPF candidate EXE generated from this branch.

Current runtime state:

- `source_material_visual_approval`: `approved`
- `visual_approved`: `true`
- `windows_owner_visual_qa`: `passed`
- `windows_owner_visual_qa_date`: `2026-08-16`
- `runtime_validation`: `passed_windows_renderer_qa`
- `runtime_approved`: `true`
- `runtime_use`: `true`
- `prototype_use`: `false`
- `production_asset`: `true`
- `normal_runtime_available`: `true`, only for the manual owner `玩一下 > 兜风` path
- `release_status`: `not_released`
- `git_status`: `runtime_approved_branch_published_draft_pr`
- `branch`: `agent/interaction-car-ride-v8`
- `initial_published_commit`: `65d7e8b09404b5dfefef1b41d5260b4cf5d0ce15`
- `draft_pr`: `#9`
- `base`: `agent/windows-runtime-assets-integration`
- `merged`: `false`
- `installer_generated`: `false`

`visual_approved=true` records source visual approval. It is separate from runtime approval. Runtime approval for v8 is granted only because the owner separately completed Windows transparent WPF renderer QA on 2026-08-16.

Approval scope is intentionally narrow: only explicit owner UI selection of `玩一下 > 兜风` may trigger `wk.interaction.car_ride` through the normal approved runtime gate.

Still forbidden for v8:

- `AutonomousTick`
- Dialogue and model routing
- command/口令 routing
- startup auto-play
- `吃一下`, `散步`, or any locked command path
- concurrent second car-ride playback

Traceability retained:

- Asset/batch: `WK-INTERACTION-CAR-RIDE-CANDIDATE-v8`
- Original ZIP SHA-256: `bf92f38e3cc976236584d8581cbb8f0f1965257c31837c0d1fd69c7670e9f7e1`
- Published directory PNG count: 253
- Manifest runtime frame references: 222
- `SOURCE-FREEZE-SHA256SUMS.txt`
- `IMPORT-VALIDATION-REPORT.json`

The formal installer has not been generated in this branch.
# Current state

Last verified: 2026-08-15 (Asia/Singapore), integration branch `agent/windows-runtime-assets-integration`.

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
- On 2026-08-26 the owner rejected the panel entry named `摸摸回应` (`wk.interaction.prone_touch`, asset `wk.interaction.prone_touch.v4.1`) and removed it from use.
- It is now `deprecated=true`, `visual_approved=false`, `runtime_validation=failed_owner_rejected`, `runtime_approved=false`, `runtime_use=false`, and `production_asset=false`.
- Autonomous, command, fallback, owner interaction, magic, car ride, and developer-forced execution are all disabled. The original PNGs and manifests remain for history, but the batch is visible only through the panel's expired-only filter.

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
- After the v4 command batch approval, this v3 batch remains visible only as an expired motion reference in the control panel. It is not selected by owner command execution.

## WK-COMMAND-PRODUCTION-CANDIDATES-v4

- Imported from local `wukong-eight-command-production-candidates-v4`.
- Contains eight owner command branches: Sit, Down, PawSit, PawProne, Jump, Spin, EatSit, and EatProne.
- The batch now has `motion_design_approved=true`, `production_asset=true`, `visual_approved=true`, `runtime_approved=true`, `runtime_use=true`, `prototype_use=false`, and `asset_stage=runtime_approved_owner_command`.
- Approved scope: explicit owner command execution from the desktop context menu and the control panel command asset page.
- Paw and Eat branch by current stable posture: sitting selects the sitting branch, prone selects the prone branch, and stand plans through sit where required.
- Forbidden sources remain: `AutonomousTick`, dialogue/model routing, startup autoplay, and unrelated interaction paths.
- The batch is shown under the control panel `普通素材 > 口令动作` group as enabled command material. The older v3 command batch remains listed there as `已过期`.

## WK-MAGIC-SPECIALS-CANDIDATE-v1

- Added the integrated Wukong-only candidate action batch under `assets/action-batches/WK-MAGIC-SPECIALS-CANDIDATE-v1/` while retaining `WK-MAGIC-SPECIALS-MOCK-v1` as historical prototype evidence.
- The active owner-preview batch contains 207 transparent RGBA PNG files: 195 reviewed 1024×1024 broom/invisibility/petrification/coin frames plus 12 existing 256×256 Scourgify mock frames pending replacement.
- The playable manifest records:
  - `wk.magic.accio_broom`
  - `wk.magic.apparate`
  - `wk.magic.petrificus_totalus`
  - `wk.magic.petrificus_release`
  - `wk.magic.scourgify`
- Accio Broom packages all eight directional loops and preserves animation phase while the desktop path changes direction; the current showcase uses reviewed takeoff and seated landing sequences.
- Apparate plays disappear, invisible relocation cut, and appear phases.
- Petrificus Totalus plays the stone transition and enters an interactive coin hold. The coin starts vivid/front, settles to flat after 800 ms, fades after 10 minutes of inactivity, and reaches exhausted after 20 minutes. Timing is configurable through `PetrifiedCoinOptions`.
- A single coin click restores vivid/front and restarts the inactivity clock. A front double-click flips to the matching-color back without resetting time; a back double-click flips to vivid/front and resets time. Pointer single-click is deferred for double-click disambiguation.
- Coin faces and all four nine-frame flip sequences are normalized to the same 1024×1024 transparent canvas. `coin-checksums.sha256` covers all 44 coin PNG files.
- On 2026-08-15, `state-01-vivid` and `state-02-flat` coin backs were updated to use the same clear double pressed rim language as the later back states while preserving the frosted face and paw mark; the vivid/flat flip frames, review preview GIF, and checksums were regenerated.
- On 2026-08-15, `state-01-vivid` and `state-02-flat` coin fronts were replaced from the local V2.3 coin master JPGs in the external `images_wk/coins` source folder; source JPGs were not committed. Matching gold-luster backs, vivid/flat flip frames, review preview GIF, and checksums were regenerated. All coin faces remain on the shared 1024x1024 transparent canvas and the batch gates remain pending/prototype-only.
- On 2026-08-15, `state-01-vivid` and `state-02-flat` coin backs were replaced from the local shared back master with the large paw embossing and crisp coin rim style; vivid/flat flip frames, review preview GIF, and checksums were regenerated. The shared 1024x1024 canvas and visible bounds remain unchanged, and gates remain pending/prototype-only.
- Batch state remains `runtime_validation=pending`, `runtime_approved=false`, `runtime_use=false`, `production_asset=false`, with `prototype_use=true`.
- Initial magic playback is allowed only through explicit owner entry points (`OwnerContextMenu` and `ControlPanel`) using `BehaviorExecutionMode.PrototypePreview` and the magic behavior whitelist. Pointer interaction is accepted only while the owner-started petrification preview is active.
- Dialogue, model output, autonomous tick, memory, personality, and normal production behavior requests must not use `PrototypePreview`; they continue to resolve through the normal runtime gate and defer until assets are formally approved.
- The batch is not added to `contracts/runtime/asset-registry.json`, not included in the autonomous pool, and not eligible for production command or model-triggered execution.
- Asset/hash/gate tests pass in the Linux checkout. The checkout does not contain a .NET SDK or Windows transparent renderer, so WPF build execution and real-renderer QA remain pending and no runtime approval is claimed.

## WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2

- Added a P2 developer-profile lifecycle candidate batch under `assets/action-batches/WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2/`.
- Source inputs were the 14-state lively daily candidate package and the recovered lifecycle microloops v2.1 package; the broken v2 ZIP was not used.
- This is a self-directed daily behavior candidate only: it does not use `wk.command.*`, is not training data, is not exposed in the normal right-click menu, and is not added to the default autonomous pool.
- Lifecycle mapping is `intro` = 14-state stand/walk/lookback/sit/prone, `loop` = prone-idle microloop, `exit` = prone-to-sit plus sit-to-stand, `interrupt_exit` = nearest stable anchor back to stand, and `fallback` = stable stand frame 01.
- Stable duplicate SHA anchors are referenced through canonical runtime frame paths where used by lifecycle phases: forward 01/stable stand, forward 10/stable sit, and forward 11/12/14 for the reverse path.
- Stand, sit, and prone idle microloops remain low-frequency developer candidates with recovered manifest timings of 7240 ms, 7680 ms, and 8900 ms per cycle.
- P2 Windows manual transparent-renderer QA passed on 2026-08-15. Batch state is now `visual_approved=true`, `art_candidate=false`, `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, and `runtime_use=true` for the autonomous lifecycle profile.
- Strict non-zero-alpha audit still records 17 frame references extending to y=932. The owner observed no visible foot jitter, transparent edge issue, or y=930/y=932 artifact in Windows renderer QA; PNGs were not cropped, recolored, resized, or baseline-shifted after acceptance.
- It is not added to `contracts/runtime/asset-registry.json`, `pet_assets.json`, production command routing, right-click menus, training data, magic, or coin flows. It is activated only in the formal autonomous lifecycle mapping: full lifecycle as a low-frequency self-directed daily behavior, stand-idle for stable stand, sit-idle for stable sit, and prone-idle for stable prone.

## 2026-08-26 lifecycle V3R1 and forward-prone V4 runtime approval

- Branch: `agent/lifecycle-v3r1-prone-front-v4-review` based on `origin/agent/personality-state-behavior-mock-v1` commit `24e2b121591cc86b7c50404970235de7b445de2c`.
- Imported `WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED` and `WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4` as two independent runtime profiles.
- V3R1 retains the historical side-prone pose. Its autonomous full lifecycle is composed as `intro -> legacy-side-prone loop -> exit`; its stand and sit microloops are additional posture-compatible daily candidates. V4 contains the independent forward-prone calm loop and one-shot lick microevent.
- No side-prone to forward-prone bridge exists. The catalog, manifests, review guide, and developer panel explicitly prohibit treating a switch between these profiles as a valid lifecycle transition.
- The V4 lick is eligible only in the V4 forward-prone profile, is one-shot, returns to its byte-identical stable anchor, and retains a 45-120 second source cooldown range with a minimum 45-second runtime gate. The profile is entered only from the approved `EatProne` terminal anchor with the same SHA-256; a generic prone state cannot hard-cut into V4.
- The active V2 lifecycle manifest and runtime bindings are unchanged. Existing command, magic, car ride, and autonomous daily review areas remain available.
- Owner Windows runtime QA passed on 2026-08-26 for all seven entries actually shown in the review panel, excluding the separately rejected `摸摸回应` batch.
- Both lifecycle batches now record `asset_stage=runtime_approved_autonomous_daily`, `visual_approved=true`, `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, `runtime_use=true`, `production_asset=true`, and `autonomous_binding_enabled=true` in their Wukong approval overlays.
- The frozen source `manifest.json`, `SHA256SUMS`, and all PNG bytes remain unchanged. V2 remains active; this approval adds V3R1/V4 choices rather than replacing V2.
- Runtime scope is autonomous daily behavior plus isolated `DeveloperPreview`. It does not add owner commands, dialogue/model routing, magic, car ride, or startup autoplay.

## 2026-08-26 lifecycle review runtime corrections

- Normal asset cards now show visual approval, runtime approval, active use, autonomous binding, deprecation, source batch, and action ID separately. Deprecated assets are hidden by default and available through an explicit expired-only filter.
- Formal autonomous runtime uses an explicit allowlist containing approved P2 lifecycle actions plus the approved V3R1 full lifecycle/stand/sit actions and the V4 forward-prone calm/lick profile. The Behavior Agent Mock uses a separate daily allowlist. Jump, spin, shake-hand, eat command, magic, and car ride cannot enter either autonomous pool; explicit owner commands and developer previews remain available where their own gates allow them.
- Pet scaling is a persistent global user scale in the 50%-250% range multiplied by the action-local presentation scale. Normal, command, magic, car ride, and coin rendering share the same user factor and preserve the bottom-center ground anchor across changes.
- Car ride startup records request-pipeline, scale calculation, first-frame decode, and first-visible timings. The first frame is shown before motion starts; only startup/current-direction frames are prefetched through the existing bounded 36-frame frozen bitmap cache.
- Accio Broom uses the pet's current monitor work area and a larger safe route targeting 20%-28% horizontal and 24%-32% vertical travel before returning near its start point. The developer panel reports measured pixel travel.
- Magic effects capture the pre-effect user scale, action-local scale, opacity, position, ground anchor, posture, and work area. Recovery restores display scale and anchor without writing alpha-bound-derived values into the global scale.

## Next implementation target

Phase 1 is the smallest honest end-to-end runtime:

`InputEvent -> Intent -> BehaviorRequest -> Eligibility -> Arbitration -> BehaviorExecution -> AnimationLifecycle -> Outcome -> RuntimeState -> Event/Memory -> Trace`

Before implementation, add or provide the reviewed UX artifact and a pinned Pupu source snapshot as read-only reference, then audit the actual source layout. Phase 1 must not expand into P1-P4 assets, complex long-term learning, cloud sync, macOS, or automatic frame generation.

## 2026-08-21 local command, lifecycle, magic, and UX candidate fixes

- Branch: `agent/personality-state-behavior-mock-v1`; repository stage: local uncommitted candidate.
- Approved v4 owner commands now hold their exact terminal frame, source batch, and visual scale for a 900 ms settle after completion, then enter the approved microloop for the declared end posture. Sit, Down, Jump, and Eat no longer fall through to an unrelated idle frame or freeze indefinitely.
- The terminal hold also carries the exact render-scale override computed for the motion that actually ran. Down, Paw, Jump, and Eat therefore do not get re-normalized from a multi-frame sequence into a larger one-frame hold.
- Autonomous playback is posture-compatible and low frequency. Startup selects stand, sit, or prone from energy, stress, and arousal instead of hard-coding prone; the default healthy state starts in stand. The approved P2 stand-idle, sit-idle, prone-idle, and full lifecycle sequences are the only basic autonomous candidates used by this change. The full lifecycle becomes eligible after minimum dwell when energy and stress permit, with cooldown, repetition suppression, and randomized scheduling rather than continuous cycling.
- The legacy red/standard basic visuals and the old prone silent-breathing runtime presentation are disabled and shown as `已过期`. Their PNGs, manifests, key poses, and motion-reference value remain intact. `WK-CORE-PRONE-IDLE-LF-v1` remains a `legacy_runtime_candidate` for comparison, with `runtime_use=false`, `motion_reference_usable=true`, and `replacement_evaluation=pending`; this change does not declare it fully superseded.
- The approved P2 lifecycle PNGs and timings are unchanged.
- The v4 Down source frame remains preserved. A versioned `down-v2` runtime copy corrects only the blue background pixels below the front leg; frames 2-12 remain byte-identical to the source sequence.
- The first two colored coin fronts and their flip transitions retain their original dimensions and alpha masks. Only pale boundary RGB contamination was corrected; coin approval gates remain unchanged.
- The P2 basic lifecycle motions and the v4 command batch share a 0.92 pet presentation scale. This changes runtime presentation only; approved P2 PNGs, hashes, baselines, and timings remain unchanged.
- Petrification treats the stone dog independently from the accepted coin size: petrify and release use the same 0.92 pet scale and a minimum 170 ms frame duration, while the coin remains at two-thirds of normal visible pet height.
- Petrification uses phase-level visual sizing: the 17-frame stone-dog intro uses scale 0.92 and the first vivid coin loop frame uses scale 2/3 before it is rendered. Later coin state and flip requests use the same 2/3 policy, removing the large-first-coin transition.
- Accio Broom and Car Ride showcase durations are selected deterministically within 10-20 seconds. Car Ride uses acceleration, longer straight segments, adjacent direction-ring turn sequences, braking, and work-area constraints.
- Control-panel spacing, typography, buttons, tabs, fields, setting rows, and toggles share refined design-token styles. No product navigation or behavior-request boundary was bypassed.
- Automated validation and a new portable EXE are required before this local candidate can be called build-verified. Windows owner visual QA is not yet claimed for these 2026-08-21 changes.

## 2026-08-24 coin edge baseline and shared autonomous bindings

- The 8 petrified coin faces now share one complete anti-aliased 900×882 visible outline on the existing 1024×1024 canvas. Transparent RGB is zeroed, contaminated pale/red/yellow cutout fringe is removed, and the complete reeded rim is preserved instead of losing pieces of the silhouette.
- All 36 flip frames were deterministically rebuilt from the repaired matching front/back state. Each flip step has identical alpha geometry across vivid, flat, faded, and exhausted states; no image generation, recoloring of the face, blur, or sharpening was used.
- `coin-checksums.sha256`, `coin-manifest.json`, the magic manifest references, and the review GIF freeze the exact candidate baseline. Magic remains `runtime_approved=false` and `runtime_use=false`; owner Windows renderer QA is still required.
- `WK-AUTONOMOUS-DAILY-BEHAVIORS-v1` now stores six semantic source bindings and zero duplicated PNG files. They resolve 59 frames from the approved v4 command and P2 lifecycle batches using source batch, behavior, phase, one-based range, and a concatenated sequence SHA-256.
- Runtime developer review resolves those shared source frames in place. Any missing, expired, disabled, disallowed, range-mismatched, or hash-mismatched source rejects the candidate. All autonomous semantic and runtime gates remain closed, so no candidate can be selected by normal autonomous ticks.
