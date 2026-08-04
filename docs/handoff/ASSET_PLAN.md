# Asset Planning and Generation

## Identity source

- Canonical profile: `wukong-current-adult-v1`.
- In the prior workflow the owner approved candidate 05 as the identity reference.
- Current generated assets are tracked in `yashawang18-coder/wukong-Desktop` draft PR #2 at exact commit `db2c29d32e4d1285711dde27b4f65d9eca084dba`.
- Real photographs are private evidence and must not be committed to a public repository.

## Evidence priority

For every new action, use this order:

1. approved identity board and closest directional anchor;
2. private real-photo evidence, when the owner provides it for correction;
3. approved keyframe for the current action;
4. approved adjacent frames from the same action;
5. legacy atlases for pose/motion only;
6. text prompt.

## Required action package

```text
assets/actions/<behavior-id>/
├── candidates/<version>/
├── approved-keyframes/
├── runtime-frames/
├── previews/
├── prompts/
├── asset.json
└── README.md
```

`asset.json` should record at least:

- stable `behavior_id` and version;
- `character_profile` and exact source commit;
- direction, equipment, start pose, end pose, and transition anchors;
- canvas size, color space, alpha policy, crop bounds, pivot, and visual scale;
- target frame count, timing/FPS, loop mode, interruption points, and variants;
- review status and owner approvals;
- prompt/version provenance, per-file hashes, preview path, and runtime mapping;
- known defects and replacement/deprecation links.

## Status lifecycle

| Status | Meaning | Runtime allowed |
|---|---|---:|
| `candidate` | Generated or edited; awaiting identity/pose review | No |
| `approved-keyframes` | Owner approved identity and key poses | No |
| `runtime-candidate` | Intermediate frames and metadata assembled | No |
| `runtime-approved` | Playback, loop, transparency, scale, and direction verified | Yes |
| `deprecated` | Retained for traceability; replaced | No |
| `rejected` | Must not be reused as a generation reference | No |

## Generation workflow

1. Define the action intent, direction, staging, start/end anchors, frame budget, loop, and interruption behavior.
2. Generate one mother keyframe using the approved identity references.
3. Obtain explicit owner approval for identity and pose.
4. Generate major keyframes as local motion deltas, preserving head geometry, markings, body scale, tail attachment, lighting, and camera.
5. Add intermediate frames between approved neighbors; do not independently regenerate every frame.
6. Remove background and normalize canvas/pivot without resizing the character inconsistently.
7. Produce an actual-speed preview and a transition preview to/from neighboring actions.
8. Run QC and record results in metadata before runtime registration.

## Mandatory QC

- identity and coat markings remain stable;
- full body is present with no accidental crop or black edge;
- alpha edge is clean and background-free;
- character scale, ground contact, pivot, and shadow do not jump;
- direction and equipment do not flip unexpectedly;
- motion path has coherent acceleration/deceleration and no duplicate-frame stall;
- loop seam and entry/exit transitions are visually acceptable;
- runtime frame order, timing, memory footprint, and manifest hashes are valid.

## Planning table

Maintain one row per action:

| behavior_id | purpose | direction | target frames/FPS | status | runtime mapping | source commit | next review |
|---|---|---|---|---|---|---|---|
| `WK-CORE-PRONE-IDLE-LF-v1` | awake prone idle | left-front | 12 frames / 8 FPS | runtime-candidate; keyframes approved | not registered; runtime_use=false | `db2c29d32e4d1285711dde27b4f65d9eca084dba` | owner preview review and real renderer QA |
