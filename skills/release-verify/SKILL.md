---
name: release-verify
description: Verify Wukong builds, contracts, assets and portable candidate output before a commit, push, PR, promotion or owner delivery. Use for release checks, not feature design.
---

# Release Verify

Use this skill before declaring a Wukong change ready for commit, publication,
candidate delivery, runtime promotion or owner acceptance.

## Read First

- `docs/handoff/BUILD_AND_RELEASE.md`
- `docs/readme/06-testing-release.md`
- `CURRENT_STATE.md` and the affected asset/batch manifests

## Verification Order

1. Inspect branch, HEAD, upstream, worktree and current diff. Preserve
   `.asset-staging/` and never stage it, build outputs, logs, raw archives or
   private portable data.
2. Run the smallest focused test for the changed behavior, then:

```powershell
dotnet build Wukong.sln --configuration Release
python -B tools\validate_contracts.py
python -B -m unittest discover -s tests -v
```

3. Run the relevant C# self-test projects. If a known test-host defect blocks an
   aggregate runner, report it truthfully, retain the focused passing evidence
   and do not call the aggregate green.
4. Run `git diff --check`, review the staged file list, and verify manifests,
   hashes, PNG inventory and approval-source invariants for asset changes.
5. For a candidate EXE, publish into a new uniquely named `.publish-check/`
   directory. Never overwrite prior owner evidence. Test a controlled launch
   only when it will not disturb an owner-owned single-instance process.
6. Distinguish build/static checks, automated tests, controlled launch, Windows
   renderer playback and owner visual QA in the report.

## Git Gate

- Fetch before changing a remote ref. Re-check that the remote target is an
  ancestor of the local commit immediately before a normal push.
- Never force-push, rebase, amend or modify `main` without explicit current
  owner authorization.
- Stage exact paths rather than `git add -A` when `.asset-staging/` or other
  local files exist.
- A successful local commit does not authorize push, PR, merge, production
  activation or installer generation.

## Finish

Report the exact commit/branch, changed files, test counts, warnings/errors,
candidate path/hash, uncommitted files, approval state and every remaining
human validation item.
