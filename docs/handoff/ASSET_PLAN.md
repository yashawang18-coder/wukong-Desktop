# Asset State and Runtime Gate

## Verified repository baseline

- Canonical identity: `assets/identity/wukong-current-adult-v1` (`approved`).
- Seventeen P0 keyframes across six action packages are owner-preview approved as `approved-keyframes`.
- Prone-idle V3 and prone-touch V4.1 are runtime candidates with owner preview evidence; real Windows renderer QA remains pending.
- Sit/stand and walk-transition intermediate sequences remain runtime candidates.
- The contract foundation defines stable poses, behavior contracts, asset sidecars, schemas, and generated P0 gaps.
- `contracts/runtime/asset-registry.json` has zero bindings. Nothing is currently runtime-enabled.

Manifest and repository facts outrank this summary. See root `CURRENT_STATE.md`, each `asset.json`, contract sidecars, and `contracts/generated/P0_GAPS.md` before changing status.

## Status lifecycle

| Status | Meaning | Runtime allowed |
|---|---|---:|
| `candidate` | Generated/edited and awaiting visual review | No |
| `approved-keyframes` | Identity and key poses approved | No |
| `runtime-candidate` | Runtime frames/metadata assembled | No |
| `runtime-approved` | Contract, playback, visual, and real-renderer gates passed | Yes, after registry binding |
| `deprecated` / `rejected` | Traceability only; must not be selected | No |

## Runtime asset contract

Formal behavior identity lives under `contracts/behaviors/`; artwork/version evidence lives in manifests and sidecars. Do not rename behavior identity when artwork is replaced, and do not overwrite approval history in place.

A production binding must declare available directions/variants, source and target pose, canvas/alpha/color rules, anchors and movement, FPS/timing, `intro`/`loop`/`exit`/`interrupt_exit`, interruption points, fallback, hashes, approval evidence, and renderer-QA evidence.

## Mandatory gates

- all referenced files exist, decode, and match hashes;
- identity, coat, geometry, equipment, direction, scale, pivot, ground contact, crop, and alpha edges are stable;
- frame order, timing, loop seam, entry/exit, interruption, fallback, and window movement are verified;
- no duplicate/translated frame is represented as a newly generated animation phase;
- asset preview and the actual desktop renderer are both tested;
- performance and recovery from missing/corrupt frames are verified;
- manifest, root state, decisions, tests, and runtime registry agree.

Do not automatically fill missing runtime frames during backend implementation. Record the gap and keep the registry closed.
