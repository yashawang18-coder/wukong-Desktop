# Autonomous patrol walk v1

Owner-approved runtime integration of the immutable 24-frame patrol gait package.

- Two 12-frame loops at 110 ms per frame.
- Right-facing frames are byte-preserved deterministic mirrors from the source package.
- Low-frequency `AutonomousTick` and explicit `DeveloperPreview` are allowed.
- Model, dialogue, command, owner menu, and startup routes remain disallowed.
- Window movement remains disabled; this approval covers in-place gait playback only.
- `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, `runtime_use=true`, `production_asset=true`.
