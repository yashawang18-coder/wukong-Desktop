# WK-AUTONOMOUS-DAILY-BEHAVIORS-v1

This is a review-only behavior-binding batch for spontaneous daily behavior. It references only the approved light-malt-gold Wukong frames from the v4 command production batch and the approved lifecycle batch.

No PNG is duplicated here. Each `wk.daily.*` action stores the immutable source batch, behavior, phase, one-based frame range, and concatenated source-byte SHA-256. The desktop catalog resolves that source range for developer review and rejects the binding if the approved source changes. The new IDs express proposed autonomous meaning; that semantic reuse still requires owner review and real Windows renderer QA. Therefore every runtime gate remains closed and the normal autonomous pool cannot select this batch.

Run `python tools/build_autonomous_daily_v1.py` to validate the approved sources and reproduce the reference-only manifest.
