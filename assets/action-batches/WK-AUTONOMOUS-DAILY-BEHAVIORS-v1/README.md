# WK-AUTONOMOUS-DAILY-BEHAVIORS-v1

This is a review-only behavior-binding batch for spontaneous daily posture transitions. It references only the approved light-malt-gold Wukong frames from the v4 command production batch and the approved lifecycle batch.

No PNG is duplicated here. Each `wk.daily.*` action stores the immutable source batch, behavior, phase, one-based frame range, and concatenated source-byte SHA-256. The desktop catalog resolves that source range for developer review and rejects the binding if the approved source changes.

The owner accepted the autonomous meaning of the four posture transitions and removed `wk.daily.playful_hop` and `wk.daily.playful_spin` from this batch. The original `wk.command.jump` and `wk.command.spin` owner commands remain unchanged. The four retained transitions are available only through explicit developer review while `visual_approved=false`, `runtime_approved=false`, `runtime_use=false`, and `may_enter_autonomous_pool_by_default=false`. Real Windows renderer QA and a separate runtime approval are still required before normal autonomous selection.

Run `python tools/build_autonomous_daily_v1.py` to validate the approved sources and reproduce the reference-only manifest.
