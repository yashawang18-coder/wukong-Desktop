# WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v13

Whole-scene road-gaze candidate for the approved v8 car ride.

## Workflow

- The approved v8 left/right complete scenes are immutable identity and geometry anchors.
- New turn poses were generated as complete dog + harness + car scenes.
- No head/neck patch, pasted head, runtime mirroring, crossfade, or AI interpolation is used.
- Generated blue-screen sources are retained under `source-generated/`.
- Deterministic chroma removal creates `raw-alpha/`; one fixed global scale and whole-frame translation produce 1024x1024 RGBA masters with wheel baseline y=900.
- Eighteen-frame left and right review sequences are assembled from complete-scene masters.

## Recovered v8 facts

- The v8 package contains approved direction/expression masters plus deterministic `build_transitions.py` processing.
- The original image backend checkpoint, sampler controls, generation seed, and fixed consistency adapter values are not present in the PNG metadata or package.
- They must not be invented. This candidate records the actual tool surface and uses explicit reference locks instead.

## Gate

This package is developer/PrototypePreview review material only:

- `visual_approved=false`
- `runtime_validation=pending_owner_windows_renderer_qa`
- `runtime_approved=false`
- `runtime_use=false`
- `prototype_use=true`
- `production_asset=false`

It must not replace v8, enter Normal runtime, AutonomousTick, Dialogue, model routing, or owner command routing before Windows owner visual QA.
