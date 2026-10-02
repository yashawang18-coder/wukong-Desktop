# Local validation - 2026-09-30

This is the earlier mechanism-only validation. The later owner-directed art
withdrawal and new candidate are recorded in `remediation-validation.md`.

Branch: `codex/decision-memory-speech-command-v2`.
Baseline HEAD: `cc32bb7093707df7c09223d869c842d9cd85be1d`.
All changes remain uncommitted. No fetch, branch switch, commit, push or merge.
Remote-tracking main remains `fcee100bfa9c7813020276d66bdf3567d45da1d6`;
this checkout has no local main branch. Remote state was not queried or changed.

## Executed checks

| Check | Result |
| --- | --- |
| `dotnet build Wukong.sln --configuration Release -m:1 --no-restore` | Passed; 6 existing warnings, 0 errors |
| Domain Console self-tests | 5/5 |
| Contracts Console self-tests | 5/5 |
| Application Console self-tests | 58/58 |
| Infrastructure Console self-tests | 23/23 |
| Desktop Console self-tests | 111/111 |
| `python tools/validate_contracts.py` | 0 errors; 9 existing lifecycle gaps |
| `python -m unittest discover -s tests -v` | 87/87 |
| `python tools/audit_companion_art.py --output docs/art-quality-v1` | 132 frames, 15 sequences, 3 references; 0 technical-error frames |
| `git diff --check` | Passed |
| Windows x64 self-contained Release folder publish | Passed; no installer |
| Publish/source SHA-256 comparison | 1729 files, including 1511 PNGs, all byte-identical |
| Reference source / staging in publish | Neither included |

Self-tests ran using `dotnet tests/Wukong.<Layer>.Tests/bin/Release/<TFM>/Wukong.<Layer>.Tests.dll`;
Desktop TFM is `net8.0-windows`, the other four are `net8.0`.
The new tests include 100 deterministic seeds over 10,000 admission decisions.
The pre-existing Infrastructure memory fixture mixed a fixed evaluation date with
wall-clock creation time; it now consistently uses a fixed clock rather than
bypassing the new future-evidence filter.

## Candidate

```powershell
dotnet publish src/Wukong.Desktop/Wukong.Desktop.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --source https://api.nuget.org/v3/index.json `
  --output .publish-check/companion-quality-mechanism-v1-candidate `
  -p:PublishSingleFile=false -p:PublishReadyToRun=false `
  -p:DebugType=None -p:DebugSymbols=false -p:RestoreBuildInParallel=false -m:1
```

EXE: `.publish-check/companion-quality-mechanism-v1-candidate/Wukong.Desktop.exe`.
SHA-256: `6a2ff785020dfd21cc537d2a47a9b89b5c445a96283ca2d41d49379d18b2e15d`.
The complete folder is required; this is not a single-file EXE.

Controlled launch used isolated `WUKONG_DATA_ROOT` with legacy migration disabled,
not the owner's profile, album or conversation history. The final run (PID 39348)
survived 20 seconds. `CloseMainWindow()` returned false, so only that exact,
path-verified process was terminated. No residual process remained. The earlier
8-second launch (PID 1200) also survived and was terminated by exact PID. These
checks do not validate graceful in-app Exit or animation quality.

`MainWindowHandle=0` is not used as a visibility verdict for a transparent WPF
window. Startup logging recorded the content-rendered event during the earlier
run, but no visual acceptance or long-duration behavior test is claimed.

Local logs: `.publish-check/companion-quality-validation/` (not a publish asset).

## Owner review still required

1. Sitting/prone command preparation follows the approved bridge with no size or
   anchor discontinuity; an incompatible front-prone bridge is deferred.
2. Ordinary autonomous behavior waits for completion, preserves compatible pose,
   and stays in a quiet episode instead of switching on every Tick.
3. Explicit quiet replies and repeat-command state exceptions are understandable.
4. Run Resting/Observing/Recovering over a longer session; automatic tests do not
   establish 30-minute visual continuity, sleep exit coverage or frame timing.
5. Art remains unchanged. Neither rejected generation attempt repairs the walking
   gait or later sleep identity in this EXE. Do not approve those attempts.
