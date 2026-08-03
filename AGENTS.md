# Wukong Desktop repository instructions

Before planning, generating code, changing assets, or preparing an EXE, read all files in `docs/handoff/`.

## Source of truth

1. Repository files and commit history are authoritative.
2. `docs/handoff/CURRENT_STATE.md` records the last verified project state.
3. Machine-readable manifests and actual test/build output outrank conversational summaries.
4. If an external asset project is referenced but its repository, branch, PR, or commit is not supplied, treat its contents as unknown.

## Required safeguards

- Do not modify `main` directly unless the owner explicitly authorizes it.
- Do not upload real photographs, credentials, signing material, private logs, or user memory.
- Never promote `candidate` to `approved-keyframes` or `runtime-approved` without explicit owner approval.
- An approved identity board does not approve an animation.
- Do not register an asset in runtime code until its transparency, canvas, continuity, loop, direction, scale, and playback have been verified.
- Do not claim that an EXE, installer, test, CI job, or release succeeded unless the corresponding command or workflow actually completed and its output was checked.
- Preserve separation between character identity, action keyframes, runtime frames, behavior logic, platform UI, and release packaging.

## Change workflow

- Use a focused `agent/<description>` branch and a draft PR.
- Update `CURRENT_STATE.md`, `DECISIONS.md`, and relevant plans whenever a change alters project facts.
- Record unresolved assumptions and blockers explicitly; do not fill gaps with plausible inventions.
- Keep code changes small enough to validate and revert independently.

