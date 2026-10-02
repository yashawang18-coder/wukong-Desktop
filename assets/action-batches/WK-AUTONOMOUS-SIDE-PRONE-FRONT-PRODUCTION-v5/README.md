# Removed Head/Neck Composite v5

The owner rejected all three head-composite sequences on 2026-09-30:
`bridge-to-front`, `side-prone-front-calm`, and `bridge-to-legacy`.

The 36 playback frames and their 13 source/review visual files were removed
at the owner's request. No other batch's images were changed.

- `REMOVAL-RECORD.json` preserves the 36 historical frame hashes, timing and phases.
- `REMOVED-FILES.json` preserves all 49 removed paths, hashes and byte counts.
- These records are historical evidence, not live runtime paths.
- Git history at the recorded commit retains the original files.
- The old composition generator and runtime composition entry were removed.
- The desktop project excludes this exact batch from build and publish.
- The remaining manifest and asset document are closed-gate tombstones.

V3R1 keeps its own approved intro, legacy-side loop and exit. No replacement
head, hard-cut front view, reversed new bridge or unapproved image is substituted.
The historical PROMPT-RECORD is retained solely to explain the failed method;
it is not an instruction to reuse local head compositing.
