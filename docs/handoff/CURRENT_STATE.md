# Current State

Last verified: 2026-08-05 (Asia/Singapore)

## Repository state

- Repository: `yashawang18-coder/wukong-Desktop`
- Default branch: `main`
- `main` remains at `d15738201aa5364d78d64679fe7b222b0a734771` and contains only the initial repository and asset-structure documentation.
- This documentation package intentionally contains no image assets, real photographs, source code, executable, installer, or signing material.
- Project assets are now available in the same GitHub repository on draft PR #2, branch `agent/assets-wukong-adult-v1-prone-idle-v1`, head `db2c29d32e4d1285711dde27b4f65d9eca084dba`.

## Confirmed design state

- Canonical identity profile name: `wukong-current-adult-v1`.
- The owner approved candidate 05 as the identity reference in the prior asset workflow.
- The approval covers identity geometry, adult proportions, muzzle/face, curled tail, coat color, and the tongue-out smile expression.
- Identity approval does not imply that any animation or runtime asset is approved.
- The owner explicitly approved the five `WK-CORE-PRONE-IDLE-LF-v1` action keyframes on 2026-08-05. Their status is `approved-keyframes`.
- A deterministic 12-frame, 8 FPS, 1500 ms loop was assembled between adjacent approved keyframes. Its status is `runtime-candidate` and `runtime_use=false`.
- Draft PR #2 contains two playback previews, a reproducible generator, manifests, and four passing validation tests.

## Not yet verified in this repository

- Source-code architecture, framework, dependency versions, and application entry point.
- Runtime asset registry and behavior-to-asset mapping.
- Windows build, test, packaging, installer, CI, update, and signing configuration.
- Any successful EXE or installer build.
- Owner runtime review of the two previews.
- Playback, scale, memory, direction, and interruption behavior in the real desktop renderer.
- Neighboring stand/prone-down/prone-up transition assets.

## Next session entry criteria

Before implementation, obtain and verify:

1. the source-code repository and target branch/PR;
2. the intended first deliverable: owner preview review, adjacent transition assets, runtime integration, or Windows build;
3. any existing CI workflow and latest real result.

After verification, update this file with commit-linked facts.
