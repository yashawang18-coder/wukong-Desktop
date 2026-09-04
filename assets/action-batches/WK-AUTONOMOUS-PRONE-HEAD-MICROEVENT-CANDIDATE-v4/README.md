# Prone head-lower and head-turn v4

This batch imports the owner's `prone-v4-candidate-review` frames without
changing their PNG bytes. The owner approved its compatible non-front prone
runtime path in the Windows renderer on 2026-09-05.

## Content

- `frames/head-lower`: 12 RGBA frames, six unique poses held for two ticks.
- `frames/head-turn`: 12 RGBA frames, six unique poses held for two ticks.
- Both sequences use a 1024 x 1024 transparent canvas and baseline y=770.
- `head-lower/frame-011.png` and `head-turn/frame-001.png` share the exact
  SHA-256 handoff `0d8f1d4b86720d1e15aee644f6a450596f025de5b55f4e697a4cb64d39b70bf2`.

The review motion plays head-lower forward, head-turn forward and backward,
then head-lower backward. This closes on the imported high-head carrier and
prevents the desktop preview from being left on the side-looking terminal
frame.

## Gate

- `visual_approved=true`
- `runtime_validation=passed_windows_renderer_qa`
- `runtime_approved=true`
- `runtime_use=true`
- `production_asset=true`
- `prototype_use=false`
- `AutonomousTick` and `DeveloperPreview` are allowed

The imported high-head start/end carrier is not byte-identical to an existing
runtime prone anchor. That audit fact remains recorded; autonomous selection
is restricted to the compatible non-front prone profile validated by the
owner. This action is not a fallback and is not valid for the forward-prone
profile.
