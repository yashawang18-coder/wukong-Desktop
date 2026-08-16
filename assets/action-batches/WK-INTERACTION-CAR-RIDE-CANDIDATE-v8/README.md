# WK-INTERACTION-CAR-RIDE-CANDIDATE-v8

Car ride v8 is the approved owner-triggered Wukong car ride runtime asset package.

## Runtime approval

- Windows owner QA passed: 2026-08-16.
- `visual_approved=true`.
- `runtime_validation=passed_windows_renderer_qa`.
- `runtime_approved=true`.
- `runtime_use=true`.
- `prototype_use=false`.
- `production_asset=true`.

This approval is intentionally narrow. The action may only be triggered by explicit owner UI selection of `玩一下 > 兜风` from the right-click menu or the control panel.

Forbidden trigger paths:

- autonomous behavior scheduling;
- dialogue or model routing;
- command / 口令 routing;
- startup auto-play;
- locked `吃一下`, `散步`, or command entries;
- concurrent second car-ride playback.

## Source and traceability

- Source ZIP: `WK-INTERACTION-CAR-RIDE-CANDIDATE-v8.zip`.
- Source ZIP SHA-256: `bf92f38e3cc976236584d8581cbb8f0f1965257c31837c0d1fd69c7670e9f7e1`.
- The ZIP file is not committed.
- Source freeze manifest: `SOURCE-FREEZE-SHA256SUMS.txt`.
- Import validation report: `IMPORT-VALIDATION-REPORT.json`.
- Runtime manifest: `manifest.json`.
- Runtime frame list: `RUNTIME-FRAMES.json`.

## Package shape

The committed directory contains 253 PNG files:

- 222 runtime frame references in `manifest.json`;
- 8 direction master PNGs;
- 5 expression master PNGs;
- 8 turn midpoint master PNGs;
- preview PNG/GIF/contact-sheet files for review.

Runtime sequences include:

- eight-direction cruising loops;
- five micro-expression loops: `head-tilt`, `happy-squint`, `side-glance`, `curious-gaze`, `knowing-look`;
- eight start transition sequences;
- eight brake transition sequences;
- sixteen adjacent turn transition sequences.

## Image contract

All runtime PNG frames are expected to remain:

- 1024x1024;
- PNG RGBA with real transparency;
- wheel baseline at `y=900`;
- free of embedded checkerboard backgrounds;
- free of green-screen residue and opaque canvas corners.

The runtime must treat each frame as a complete Wukong plus car image. It must not use partial upper-body overlays, crossfade, ghosting, motion blur, runtime interpolation, or mirrored substitute frames to hide transition problems.

## Runtime integration notes

`wk.interaction.car_ride` uses the normal owner-triggered behavior path after approval. It no longer depends on `PrototypePreview`, but the prototype preview infrastructure remains available for other candidate assets.

The runtime must keep the existing safety boundaries:

- only `OwnerContextMenu` and `ControlPanel` may request this action;
- `AutonomousTick`, dialogue, model routing, and commands must be rejected or deferred;
- repeated clicks must not start a second player;
- stop must brake or recover safely;
- exceptions must restore the pet to a visible idle state;
- decoded frame caching must stay bounded and must not retain all 222 runtime frames at once.

The formal installer has not been generated for this branch.