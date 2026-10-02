# Walking v8

Owner accepted the normalized art and authorized runtime integration and whole-frame
left/right mirroring on 2026-10-02. Thirteen unique 1024x1024 RGBA PNGs are copied
byte-for-byte from the v8 normalized review, without any further image processing.
Original v7/native sources and reviews remain in the local art review directory.
The source manifest hash, selected PNG hashes and prior common normalization
parameters are preserved in this batch. `asset.json` is identical to `manifest.json`.

Existing left/right patrol behavior IDs remain stable. Left is canonical; right is
one WPF ScaleX=-1 transform around the canvas center. No mirrored PNG duplication.
The complete request locks its orientation, scale and anchor. Three phases:
intro 1060ms, eight-frame loop 1600ms per cycle, exit 1440ms. Autonomous walks run
2-4 complete cycles. Normal stop waits for the current loop boundary then exits;
application shutdown and high-priority replacement cancel immediately and recover.
A single fixed group render scale (0.68) applies to both directions and all phases.
It does not alter source PNGs or apply per-frame fitting.

The manifest is authoritative for runtime approval. Owner source-art approval and
automated real-WPF playback are separate evidence. Final owner desktop review of
natural transitions, actual scale, mirroring and gait remains a delivery check.
The old patrol package stays in source history but is no longer resolved or published.
