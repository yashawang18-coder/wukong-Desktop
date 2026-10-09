---
name: asset-promotion
description: Promote a reviewed Wukong action batch through explicit visual and Windows-runtime gates without broadening sources, autonomy, or unrelated asset approval. Use only after owner authorization.
---

# Asset Promotion

Use this skill to move one named asset/action from review or candidate state to a
specific approved runtime scope. It is not an import or a visual-art repair skill.

## Read First

- `assets/AGENTS.md`
- The batch `asset.json`, manifest, SHA inventory and existing runtime binding
- `CURRENT_STATE.md`, `DECISIONS.md` and focused approval tests
- `docs/handoff/BUILD_AND_RELEASE.md`

## Gate Model

Treat these as independent facts:

1. Source integrity and visual owner approval.
2. Windows renderer behavior for size, anchor, alpha, timing and continuity.
3. Runtime approval for a named source and execution mode.
4. Runtime use for named sources, poses, Episodes and autonomy.

Do not set a later state merely because an earlier state is true.

## Workflow

1. Identify the exact batch, action IDs, source paths, current gates and active
   catalog/registry binding. Do not infer identity from display name.
2. Verify owner authorization scope: manual owner action, developer preview,
   autonomous Episode, dialogue or another source are separate approvals.
3. Run asset integrity checks and the exact Windows WPF playback route. Confirm
   compatible entry/exit poses, stable effective scale/baseline and safe
   interruption behavior.
4. Update only the relevant state fields, source policies and capability catalog
   entries. Keep forbidden sources explicitly closed rather than relying on
   absence from a menu.
5. Update the manifest/asset metadata plus `CURRENT_STATE.md` and `DECISIONS.md`
   in the same change. Preserve source frames, historical candidate state and
   SHA records.
6. Add regression coverage for allowed and forbidden sources, missing-frame
   failure, pose compatibility and preview isolation.

## Invariants

- Approval never creates an implicit fallback, hard splice, mirror, rescale or
  asset-byte rewrite.
- Autonomous registration is stricter than owner manual approval. Use explicit
  capability and Episode allowlists.
- `DeveloperPreview` and `PrototypePreview` never write production state,
  relationship, memory or learning.

## Finish

Report the exact allowed trigger sources, remaining closed routes, gate values,
Windows evidence and any owner review still required.
