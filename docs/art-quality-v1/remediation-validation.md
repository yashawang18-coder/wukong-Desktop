# Art Remediation Validation - 2026-09-30

Branch: `codex/decision-memory-speech-command-v2`.
HEAD: `cc32bb7093707df7c09223d869c842d9cd85be1d`, unchanged.
No fetch, switch, commit, push, merge, main update or installer.

## Executed

| Command/check | Actual result |
| --- | --- |
| `dotnet build Wukong.sln --configuration Release --no-restore` | Passed, 0 errors, 6 existing warnings |
| Domain Console self-tests | 5/5 |
| Contracts Console self-tests | 5/5 |
| Application Console self-tests | 58/58 |
| Infrastructure Console self-tests | 23/23 |
| Desktop Console self-tests | 111/111 |
| `python -m unittest discover -s tests -v` | 87/87 |
| Final reference-selection targeted Python tests | 2/2 |
| `python tools/validate_contracts.py` | 0 errors, 9 existing lifecycle gaps |
| `python tools/audit_companion_art.py --output docs/art-quality-v1` | 96 frames, 12 sequences, 3 references; no technical file errors |
| `git diff --check` | Passed |
| Windows x64 self-contained Release folder publish | Passed |
| Published source-resource SHA comparison | 1678 files / 1463 PNGs, zero mismatches |
| Removed side-prone v5 in publish | Absent |
| Raw generated masters in publish | Not included |

Self-test command form:
`dotnet tests/Wukong.<Layer>.Tests/bin/Release/<TFM>/Wukong.<Layer>.Tests.dll`.
Desktop TFM is `net8.0-windows`; other layers use `net8.0`.
The changed sleep tests continue full 48-frame byte/hash/decode/timing validation;
they now require all eight records to be non-executable, not just four.
The composite tests assert removal, historical hashes and publish exclusion.

## Candidate

```powershell
dotnet publish src/Wukong.Desktop/Wukong.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true --source https://api.nuget.org/v3/index.json --output .publish-check/art-remediation-v2-candidate -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -p:RestoreBuildInParallel=false -m:1
```

EXE: `.publish-check/art-remediation-v2-candidate/Wukong.Desktop.exe`.
EXE SHA-256: `6a2ff785020dfd21cc537d2a47a9b89b5c445a96283ca2d41d49379d18b2e15d`.
Desktop DLL SHA-256: `027536c2a31e434a0ec2fb5c289f4953d94f18a89f38cec27a74bfc8bf84ab27`.
This is a folder build, not a single-file bundle. The native EXE host hash can
remain unchanged when managed code changes; the DLL hash identifies this build.
Do not move the EXE without its DLLs, assets and dependencies.

Controlled launch PID 48872 survived 20 seconds with isolated WUKONG_DATA_ROOT
and a marker preventing legacy user-data migration. CloseMainWindow returned false.
The exact PID was path-verified, terminated, and confirmed absent. No unrelated
process was terminated. MainWindowHandle=0 was not used as a visibility verdict.
Evidence: `.publish-check/art-remediation-v2-validation/launch.json`.

This proves startup survival, not graceful in-app Exit or Windows animation QA.
No new sleep/walking replacement is included: generated masters failed technical
gates. Patrol remains the existing sequence and is not claimed repaired.
Owner review is still required for preserved lifecycle continuity and Agent behavior.

## File Integrity Boundaries

Only the explicitly rejected v5 batch's 48 PNGs and one GIF were deleted.
No retained PNG was modified. Old sleep v10 pixels remain byte-identical.
The old compositor source was removed; other generators are untouched.
The prior candidate folder and .asset-staging input are untouched.
All pre-existing uncommitted mechanism work remains present.
