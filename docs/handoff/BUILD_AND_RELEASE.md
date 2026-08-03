# Build and Release

The current repository does not yet verify a source tree or build system. The following is the required release contract, not a claim that these steps already exist or pass.

## Required pipeline

1. Restore dependencies from locked/versioned definitions.
2. Validate code formatting and static analysis.
3. Run unit and integration tests.
4. Validate every runtime asset manifest, file hash, alpha/canvas rule, and behavior mapping.
5. Build the Windows application for the declared architecture(s).
6. Package a versioned installer and include licenses/notices.
7. Install on a clean Windows environment, launch, exercise smoke tests, uninstall, and check upgrade behavior from the previous supported version.
8. Publish immutable checksums and retain CI logs/artifacts according to policy.

## Release metadata

Record:

- source repository, branch, and commit SHA;
- external asset repository and commit SHA;
- application version and asset-registry version;
- SDK/toolchain and dependency lock versions;
- target Windows versions and architectures;
- CI workflow/run URL, test summary, artifact name, size, and SHA-256;
- signing identity/status without committing private keys;
- installer, upgrade, rollback, and known-issue results.

## Release gates

- No `candidate` or `runtime-candidate` asset is reachable from production behavior mappings.
- A missing asset produces a safe fallback rather than a crash or invisible pet.
- Transparent-window input, scaling, multi-monitor/DPI, sleep/wake, restart, and settings recovery are smoke-tested.
- Logs and local memory do not leak credentials, private images, or conversation content.
- An artifact is called “built” only after the build completes; “released” only after the intended publication action completes.

## First source audit

When source code becomes available, replace this generic contract with exact commands for restore, test, build, publish, installer generation, CI invocation, artifact download, and clean-machine verification.

