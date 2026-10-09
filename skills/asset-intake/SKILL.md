---
name: asset-intake
description: Import a new Wukong action-material batch from approved local input while preserving source bytes, provenance, candidate gates and auditability. Use for ZIP or folder imports, not runtime promotion.
---

# Asset Intake

Use this skill when bringing a new owner-supplied asset batch into the repository
as a versioned candidate or review package.

## Read First

- `assets/AGENTS.md`
- `ASSET_STRUCTURE.md`
- `CURRENT_STATE.md` and `DECISIONS.md`
- The closest existing batch's `asset.json`, manifest and focused tests
- `docs/handoff/ASSET_PLAN.md` when the batch affects planned behavior

## Workflow

1. Inspect branch, HEAD, worktree and `.asset-staging/`. Preserve all existing
   user files; staging input is never edited, deleted or committed.
2. Verify the supplied archive/file name, SHA-256, internal inventory, dimensions,
   image mode, decodeability and source manifest before copying anything.
3. Create a unique versioned directory under `assets/action-batches/`. Preserve
   original production frame bytes unless the owner explicitly authorizes a
   deterministic conversion workflow.
4. Keep review GIFs, contact sheets and generated inspection images out of the
   runtime sequence. Record source hashes, per-frame paths, timing, bounds,
   baseline, anchors and source-package facts in the repository-side manifest.
5. Set candidate gates truthfully. Source/visual confirmation does not grant
   `runtime_approved`, `runtime_use`, `production_asset` or autonomous binding.
6. Register only the requested DeveloperPreview or isolated candidate path. Do
   not substitute, replace or fallback to old art when a new batch is missing.
7. Add deterministic inventory, SHA, RGBA, alpha, timing, path and gate tests.
   Update `CURRENT_STATE.md` and `DECISIONS.md` whenever the batch's durable
   state changes.

## Invariants

- Never mix visual sources from batches or splice/repaint part of a dog.
- Never infer runtime approval from static QA, GIF review, CI or a successful
  EXE launch.
- Do not alter an approved batch in place. Create a new version and retain old
  assets as audit evidence when retirement is requested.
- Fail closed on missing, hash-mismatched, expired or incompatible material.

## Finish

Run focused asset checks, contract validation and the relevant desktop tests.
State separately what was statically verified, what ran in a real Windows WPF
renderer, and what still needs owner review.
