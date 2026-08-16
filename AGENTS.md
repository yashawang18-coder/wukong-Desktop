# Wukong Desktop Agent Instructions

## Read before working

Before making changes, read:

1. The applicable `AGENTS.md` files from the repository root to the working directory.
2. `CURRENT_STATE.md`.
3. `DECISIONS.md`.
4. `ASSET_STRUCTURE.md`.
5. The relevant `asset.json`.
6. Tests covering the affected area.
7. The relevant files in `docs/handoff/` for product, behavior, migration, and release boundaries.

Repository files and executable behavior are the source of truth. Do not infer completed work from conversation history alone.

## Scope discipline

- Change only what the owner explicitly requested.
- Preserve unrelated code, assets, metadata, dimensions, timing, and behavior.
- Do not silently expand a diagnostic request into implementation.
- Never claim that a build, test, animation, or interaction passed unless it was actually executed.
- Clearly distinguish: statically inspected, automated-test verified, CI verified, and Windows real-renderer verified.

## State documents

- `CURRENT_STATE.md` records the current factual state and remaining blockers.
- `DECISIONS.md` records durable owner decisions and their reasons.
- `AGENTS.md` contains stable working rules only; do not store temporary task progress here.
- Any asset-state change must update its `asset.json`.
- Material approval or runtime-state changes must update `asset.json`, `CURRENT_STATE.md`, and `DECISIONS.md` in the same commit.

## Testing and completion

- Run the smallest relevant test first, then the broader affected suite.
- Add or update automated tests when introducing a new invariant.
- Review the final diff for unrelated files and generated caches.
- Do not commit `__pycache__`, temporary previews, intermediate masks, or local logs.
- "Done" requires:
  1. requested files changed;
  2. relevant tests executed;
  3. results reported accurately;
  4. remaining real-renderer validation identified;
  5. repository state documents synchronized when state changed.

## Git safety

- `main` is protected. Do not commit, push, or merge into `main` without explicit owner authorization.
- Never force-push.
- Prefer a real Git checkout and one coherent commit.
- Before publishing, fetch and record the target branch HEAD.
- Immediately before moving the remote branch, verify its HEAD again.
- Update the branch only by non-force fast-forward.
- If the remote HEAD changed, stop and reconcile instead of overwriting it.
- A local commit does not imply authorization to push.
- Public uploads require explicit repository, branch, and public-release scope.
- When a real checkout is unavailable, use content-addressed blob deduplication, one tree, one commit, and one final non-force ref update.
- After publishing, verify the remote commit SHA, PR head branch, affected file set, and that `main` remained unchanged.

## Code review rules

Flag changes that:

- promote an asset without the required approval state;
- claim tests or Windows playback that were not actually run;
- change unrelated visual or runtime behavior;
- modify approved source assets in place without a new version;
- omit synchronized manifest or state-document updates;
- weaken non-force Git publishing safeguards.

## Product and architecture boundaries

- Wukong is not a button-to-animation player. UI, menu, autonomous scheduling, and model output submit a `BehaviorRequest`; none may start an animation directly.
- Behavior requests pass through intent resolution, eligibility, arbitration, execution, outcome, state update, event/memory recording, and developer trace.
- The model may propose natural-language replies, semantic behavior intent, and memory candidates, but it may not mutate state, name asset files, or bypass eligibility and safety rules.
- Preview, simulation, and developer-forced runs use isolated contexts and must not write production state or memory.
- `reference/pupu/`, if supplied, is read-only engineering reference. Pupu behavior IDs, cat assets, hard-coded animation mappings, user data, and secrets must not enter Wukong runtime or release output.
- The six-tab UX is a product contract, not the behavior engine. Normal mode hides raw state and internal traces; developer mode exposes diagnostics without bypassing production rules.





## Phase 1 working boundaries

- `reference/pupu-source/` is a read-only engineering reference.
- Never modify files under `reference/pupu-source/`.
- Never copy Pupu cat assets, behavior IDs, animation mappings, user data,
  API credentials, memories, albums, build artifacts, or generated binaries
  into the Wukong implementation.
- Pupu may only inform Windows desktop hosting, input capture, configuration,
  diagnostics, testing, installer, and other infrastructure decisions.
- Pupu behavior selection, animation orchestration, state mutation, memory
  feedback, and model-triggered actions must be redesigned for Wukong.
- `docs/ux/wukong-ux.html` defines page layout and user interaction intent.
- `docs/DECISIONS.md`, behavior contracts, and state-machine documents define
  behavioral and architectural constraints.
- If UX and architecture documents conflict, record the conflict and ask;
  do not silently choose one.
- Only Wukong assets may enter the runtime asset registry.
- Approved keyframes are not automatically runtime-approved animations.
- UI and model responses must submit BehaviorRequest; they must not directly
  play animations or write runtime state.
- Do not modify `main`.
- Work on the current feature branch.
- Do not commit, push, merge, or create a pull request without explicit approval.
- Before editing, inspect the repository and existing user changes.
- After editing, run relevant validation, tests, and review the Git diff.
- Never claim Windows real-machine verification unless it was actually run
  on Windows 10 or 11.
