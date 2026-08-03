# Code Generation Guide

## Before generating code

1. Read `AGENTS.md` and all handoff documents.
2. Inspect the actual repository tree, project files, dependency locks, entry points, tests, CI, and current diff.
3. Verify external asset and source commits; do not infer missing contents from chat history.
4. State the proposed change boundary, affected modules, observable behavior, test plan, and rollback path.

## Implementation rules

- Prefer domain types and interfaces over UI event-handler logic.
- Keep behavior decisions deterministic under a supplied seed and clock.
- Load assets through a validated registry/manifest abstraction.
- Validate manifests at startup or build time and expose actionable diagnostics.
- Use explicit state-transition commands/events instead of unrestricted property mutation.
- Make cancellation and interruption first-class for long-running actions.
- Keep file paths, timing, thresholds, and weights in versioned configuration where appropriate.
- Add migrations for persisted schema changes.
- Preserve backward compatibility deliberately; document removals and deprecations.

## Definition of done for a code change

- Code builds in the real target configuration.
- Unit/integration tests relevant to the change pass.
- Manual behavior or visual checks are documented where automation is insufficient.
- Logs show enough evidence to diagnose selection, transition, asset, and persistence failures.
- Handoff and decision documents reflect new facts.
- A draft PR clearly separates verified results from untested claims.

## Prohibited shortcuts

- hard-coded absolute asset paths;
- registering candidate assets as runtime-ready;
- silently substituting a different identity version;
- coupling the behavior core to WPF/window classes or a specific renderer;
- swallowing asset-load, state-migration, or animation failures;
- claiming CI/build/install success from static inspection alone.

