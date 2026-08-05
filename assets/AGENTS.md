# Asset Workflow Instructions

## Privacy and identity

- Never commit Wukong's real photographs to this public repository.
- Use the approved identity board and direction anchors as identity truth.
- Legacy assets may guide motion or pose, but must not override the current identity.
- Do not independently regenerate a full character when a local correction is sufficient.

## Minimal visual changes

When the owner identifies a specific defect:

- Modify only the named property or region.
- Preserve every unrelated property, including body scale and proportions, pose and gait phase, face and expression, coat color, paws, tail, silhouette, canvas dimensions, placement, ground baseline, and alpha outside the edited area.
- Prefer deterministic transforms, masked compositing, or local color mapping for single-variable corrections.
- Reject an edit if it introduces changes outside the requested scope.
- Do not use a visually similar full-frame redraw as a replacement.

## Approval lifecycle

Use this lifecycle only:

`candidate` → `approved-keyframes` → `runtime-candidate` → `runtime-approved`

Visual owner approval means only:

- `owner_preview_approved=true`
- `runtime_validation=pending`
- `runtime_approved=false`
- `runtime_use=false`

Never infer runtime approval from an approved identity board, approved keyframes, a GitHub GIF, automated image checks, or CI success without real application playback.

Only successful playback in the real desktop renderer may promote an asset to `runtime-approved` and permit `runtime_use=true`.

## Approved asset immutability

- Approved keyframes are versioned anchors.
- Do not overwrite approved bytes after owner approval.
- If a correction is required, create a new version and preserve provenance.
- Runtime interpolation must derive from approved anchors.
- Runtime generation must not silently change identity, body scale, or ground contact.

## Required asset validation

Before commit or upload, validate in one pass:

- every PNG decodes successfully;
- format is PNG and mode is RGBA;
- expected dimensions;
- transparent canvas edges;
- non-empty alpha bounds;
- ground baseline consistency where applicable;
- declared file size;
- SHA-256;
- unique and valid manifest paths;
- expected frame count;
- relevant automated tests.

For animated GIF previews also validate exact frame count, per-frame durations, total duration, and expected dimensions.

## Source and preview policy

- Runtime source PNGs remain lossless at their approved resolution.
- GitHub review GIFs are separate derivatives.
- Downscale review GIFs to 256 or 384 pixels.
- Revalidate frame count and total duration after GIF compression.
- Preview approval does not approve the underlying sequence for runtime use.

## Asset commit contents

An asset-state commit must include, when applicable:

- source or runtime frames;
- the action `asset.json`;
- batch approval or transfer manifest;
- SHA-256 and file-size metadata;
- relevant automated tests;
- `CURRENT_STATE.md`;
- `DECISIONS.md`.

Do not commit ZIP duplicates, temporary composites, masks, local review boards, or rejected generation attempts unless they have explicit audit value.
