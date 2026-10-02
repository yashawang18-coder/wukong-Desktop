# Walk v8: shared normalization and material review

## Scope

Local art-only follow-up on `codex/decision-memory-speech-command-v2`, baseline
`cc32bb7093707df7c09223d869c842d9cd85be1d`. Existing dirty files are preserved.
No application code, approved source art, runtime binding or existing EXE changes.

Review root: `.publish-check/art-rebuild-walk-v8-normalized-review/`.

- `originals-v7/`: 13 byte-preserved prior images.
- `raw-selected/`: 13 selected native images; only 3/4/6 are new material edits.
- `frames/`: 13 separate 1024x1024 RGBA runtime-format copies.
- `manifest.json`, `SHA256SUMS.txt`, `generation-prompts.json`, `validation.json`:
  exact processing parameters, provenance, selected/rejected attempts and checks.
- `review.html`: synchronized before/after, full start/two loops/stop, individual
  phases, 128/192/256/512/1024 canvas sizes and light/dark/checker backgrounds.
- `review/`: lossless review animations, contact sheets and material/paw details.

## Shared Processing

One scale `1024/1254`, translation `(0,0)`, whole square canvas, no crop or fit.
Remove alpha=1 and detached components with maximum alpha <=8, identically before
and after resize. Float32 premultiplied-alpha LANCZOS; output straight RGBA with
zero RGB under alpha zero. No scripted recoloring, sharpening or body-part patches.
The common `(512,900)` anchor is metadata, not a per-frame pixel adjustment.

## Evidence and Limits

13/13 specialized tests passed, including after repository copy: source-byte
preservation, anchors, shared parameters, pixel reproducibility, strict transparent
edges, component cleanup, protected torso/neck/nose areas, frame hashes, timing,
shared entry/exit images and closed approval flags. No new holes in the projected
opaque source core. Standing front-leg negative space is retained and annotated.

Intro: 1060 ms; eight-frame cycle: 1600 ms; exit: 1440 ms. Full review with two
cycles is 5700 ms. Lossless WebP merges one repeated boundary image into a longer
hold, retaining the exact total time: 24 timeline entries, 23 display frames.

Frame 3's sampled neck/flank color distance improved. Frame 4/6 fur revisions
are visually proposed, but sampled color metrics are not uniformly better. The
package does not claim all flicker is eliminated. Foot-bottom coordinates range
893..907 on the 1024 canvas, a 2.625-pixel span at a 192 canvas. This is measured
variation, not a physical weight/center-of-mass proof. No per-frame transform
was used to conceal it. Review 8->1, paw contacts, the flexed start paw, stop
weight transfer and texture at real display size.

All approval/use flags remain false. Browser/image review is not WPF QA.
No EXE was built in this art-only turn; the earlier code-only candidate still
uses the old patrol art. No commit or push.
