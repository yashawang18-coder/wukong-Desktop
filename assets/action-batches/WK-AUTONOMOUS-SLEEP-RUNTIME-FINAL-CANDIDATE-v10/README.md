# Wukong sleep runtime final v10

This candidate contains only the 48 transparent PNG files supplied in `wukong-sleep-runtime-final-transparent-v10.zip`. Files are copied byte-for-byte; no frame is generated, recolored, resized, cropped, filtered, or re-encoded.

The source archive contains eight sequences and no manifest, report, GIF, or timing metadata. Preview timing retains the existing stable sleep semantics: the 16-frame lifecycle uses 260 ms for F01-F15 and 1100 ms for F16; the eight-frame roll uses 260 ms for F01-F07 and 800 ms for F08; breathing loops use 650 ms per frame.

Owner Windows renderer QA passed on 2026-09-06 for the four non-deprecated sequences. They use `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, `runtime_use=true`, `production_asset=true`, and `prototype_use=false`. The complete lifecycle and front breathing loop enter the low-frequency autonomous pool only from compatible prone profiles. The independent roll and left-side breathing loop remain approved developer previews until their required runtime pose bridges exist.

Runtime and panel preview sizing use one fixed scale per sequence. Alpha-visible bounds provide the baseline, then owner Windows visual review supplies the pose-specific correction because equal bounds do not imply equal perceived body mass. The renderer never changes scale between frames. The main lifecycle uses `0.61`; the roll uses `0.64`; front and left-side breathing use `0.63` and `0.78`. Source PNG bytes remain unchanged.

Owner review rejected the right-side sprawled, compact prone, curled side, and top-down breathing variants for inconsistent color and fur texture. They remain as immutable audit evidence with `deprecated=true`, `runtime_validation=failed_owner_visual_qa`, no allowed request source, and no developer playback. The default gallery hides them; the expired filter preserves their static preview and metadata.

The v5 front-three-quarter-side and right-rear breathing views are absent from v10 and are not carried forward. The main lifecycle already includes its roll; the independent roll must not be appended. Incompatible camera views must not be hard-cut together. No approved wake or interrupt-exit sequence exists, and legacy sleep artwork is not an allowed fallback.
