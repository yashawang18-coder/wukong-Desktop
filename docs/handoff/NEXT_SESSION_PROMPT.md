# Next Session Prompt

Copy and fill the bracketed values:

```text
Continue the Wukong desktop-pet project. Do not infer current state only from chat history.

Documentation repository:
https://github.com/yashawang18-coder/wukong-Desktop

Source-code repository/ref:
[URL + branch/PR + commit SHA, or “not yet available”]

External asset project/ref:
[URL + branch/PR + commit SHA]

Current target:
[one precise deliverable]

First perform a read-only audit:
1. Read root AGENTS.md and every file in docs/handoff/.
2. Inspect the actual repository tree, current/default branches, latest commits, open PRs, asset manifests, and CI state.
3. Verify the supplied source and asset commit SHAs exist.
4. Report: completed; owner-approved but not integrated; candidate/pending review; pending development; build/release blockers.
5. Clearly separate repository facts, verified command/workflow results, owner statements, and remaining assumptions.

Rules:
- Do not modify main without explicit approval.
- Do not upload real photographs or private data.
- Do not promote an asset status without explicit owner approval and required QC.
- Do not register non-runtime-approved assets.
- Do not claim an EXE, installer, CI job, or release succeeded unless actually executed and checked.
- Before writing code, present the affected modules, acceptance criteria, tests, and rollback boundary.

After the audit, update CURRENT_STATE.md with verified facts and continue only the stated target on a focused branch/draft PR.
```

Recommended target examples:

- “Audit and continue `WK-CORE-PRONE-IDLE-LF` from its exact external asset commit.”
- “Integrate a named `runtime-approved` action through the runtime asset registry.”
- “Audit the Windows source tree and implement reproducible CI packaging without claiming installation success until verified.”

