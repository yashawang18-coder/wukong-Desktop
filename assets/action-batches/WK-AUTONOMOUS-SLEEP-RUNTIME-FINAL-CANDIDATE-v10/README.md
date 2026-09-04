# Wukong sleep runtime final v10 candidate

This candidate contains only the 48 transparent PNG files supplied in `wukong-sleep-runtime-final-transparent-v10.zip`. Files are copied byte-for-byte; no frame is generated, recolored, resized, cropped, filtered, or re-encoded.

The source archive contains eight sequences and no manifest, report, GIF, or timing metadata. Preview timing retains the existing stable sleep semantics: the 16-frame lifecycle uses 260 ms for F01-F15 and 1100 ms for F16; the eight-frame roll uses 260 ms for F01-F07 and 800 ms for F08; breathing loops use 650 ms per frame.

Runtime gates remain closed: `runtime_validation=pending_owner_windows_renderer_qa`, `runtime_approved=false`, `runtime_use=false`, `production_asset=false`, and `prototype_use=false`. Only isolated `DeveloperPreview` is allowed.

The v5 front-three-quarter-side and right-rear breathing views are absent from v10 and are not carried forward. The main lifecycle already includes its roll; the independent roll must not be appended. Incompatible camera views must not be hard-cut together. No approved wake or interrupt-exit sequence exists, and legacy sleep artwork is not an allowed fallback.
