# Build and Release Contract

## Current fact

The consolidated repository contains assets, contracts, tools, and Python tests, but no Windows application project or installer configuration. The commands below are requirements for the future source tree, not claims of current success.

The currently verified repository test command is:

```text
python3 -m unittest discover -s tests -v
```

On 2026-08-09 it passed 25 tests against `main` commit `2f7e949c21bd88d2a4cc49977778f4c517dd962a`.

## Required pipeline

1. Restore locked dependencies and validate formatting/static analysis.
2. Run domain, persistence, contract, asset, and UI-boundary tests.
3. Validate manifests, hashes, approval gates, runtime bindings, and exclusion of Pupu/reference/private data.
4. Build the declared Windows architecture and configuration reproducibly.
5. Package a versioned installer with notices, data-directory policy, upgrade/rollback behavior, and checksums.
6. On clean Windows 10/11 machines, install, launch, test transparent-window input/DPI/multi-monitor/sleep-wake/restart, exercise one completed and one interrupted P0 behavior, upgrade, uninstall, and confirm user-data policy.
7. Record CI/workflow URL, source SHA, asset registry version, toolchain, test summary, artifact identity/SHA-256, signing status, and real-machine evidence.

## Release gates

- Only `runtime-approved` assets with complete registry bindings are reachable.
- Missing/corrupt assets fail safely without an invisible pet or process exit.
- Owner UI, autonomous scheduling, and model intent share arbitration.
- Preview/simulation never contaminate real memory.
- Secrets, conversations, private images, and Pupu/reference data are absent from logs and outputs.
- “Built”, “CI verified”, “installed”, and “real-renderer verified” are separate evidence-backed statements.
