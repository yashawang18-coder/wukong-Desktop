# WK-AUTONOMOUS-DAILY-BEHAVIORS-v1

This is an owner-approved behavior-binding batch for spontaneous daily posture transitions. It references only the approved light-malt-gold Wukong frames from the v4 command production batch and the approved lifecycle batch.

No PNG is duplicated here. Each `wk.daily.*` action stores the immutable source batch, behavior, phase, one-based frame range, and concatenated source-byte SHA-256. The desktop catalog resolves that source range for developer review and rejects the binding if the approved source changes.

The owner accepted the autonomous meaning and Windows renderer result of the four posture transitions on 2026-09-05. They are enabled for low-frequency `AutonomousTick` selection and explicit `DeveloperPreview`. `wk.daily.playful_hop` and `wk.daily.playful_spin` remain excluded; the original `wk.command.jump` and `wk.command.spin` owner commands remain unchanged and command-only.

The approved state is `visual_approved=true`, `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, `runtime_use=true`, and `may_enter_autonomous_pool_by_default=true`. No PNG is duplicated or modified by this approval.

Run `python tools/build_autonomous_daily_v1.py` to validate the approved sources and reproduce the reference-only manifest.
