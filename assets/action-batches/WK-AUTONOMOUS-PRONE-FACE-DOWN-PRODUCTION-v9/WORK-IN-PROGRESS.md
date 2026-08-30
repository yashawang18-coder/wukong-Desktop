# Wukong prone face-down v9 — production checkpoint

This directory is an input-freeze checkpoint, not a production candidate.

## Frozen authority inputs

- `PROMPT-RECORD.md` is the owner-supplied v9 production contract.
- `source/approved-down-v2-terminal-anchor.png` is the exact approved F01/F36
  RGBA anchor (`c2a6f39a5d3f4db3d14fd80b9f4e8695add95c5e4a5d3e827de83e64b4a5f44d`).
- `source/owner-approved-face-down-pose-reference.png` is a pose-only reference
  and is not an approved identity, face, fur, body, or runtime pixel source.

## Current status

No v9 production frames, GIFs, manifests, runtime mappings, approvals, or ZIP
are present in this checkpoint.  The package must not be interpreted as
`production_candidate_owner_qa_pending` until the complete 36-frame output and
all review artifacts exist and pass continuous visual review.

All approval and runtime gates remain false:

- `visual_approved = false`
- `runtime_approved = false`
- `runtime_use = false`
- `production_asset = false`

## Rejected local probes

The following approaches were tested outside this repository and rejected;
their output frames are intentionally not committed:

1. independent per-frame generation — identity, eye color, and fur drift;
2. whole-frame crossfade/optical flow — double eyes, ears, paws, and silhouettes;
3. single-source TPS/piecewise mesh — face distortion, transparent tears, and
   missing shoulder/chest occlusion pixels;
4. rigid head/chest layers — stable identity and eyes, but anatomically false
   neck/chest motion and an abrupt open-mouth-to-closed-mouth transition;
5. generated endpoint plus deterministic local layers — paw drift, shoulder
   seams, hard baseline clipping, and embossed fur.

The next permitted production attempt is a single temporally coherent
first-frame/last-frame animation render.  It must use the frozen F01 and one
owner-reviewable F11, after which the selected settle frames are normalized to
the v9 contract and F25–F36 are created only by exact byte reversal.

This checkpoint does not modify `main`, create a runtime binding, or authorize
merging.
