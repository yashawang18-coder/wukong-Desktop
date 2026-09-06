# Autonomous patrol walk v1

Owner-approved runtime integration of the immutable 24-frame patrol gait package.

- Two 12-frame loops at 110 ms per frame.
- Right-facing frames are byte-preserved deterministic mirrors from the source package.
- Low-frequency `AutonomousTick` and explicit `DeveloperPreview` are allowed.
- Model, dialogue, command, owner menu, and startup routes remain disallowed.
- The local review build translates the pet in the matching left/right direction and constrains the route to the current work area.
- Gait pixels retain their existing approval. Owner Windows review passed for the bounded desktop translation on 2026-09-06, recorded as `window_motion_validation=passed_windows_renderer_qa`.
- Both directions use one fixed `runtime_render_scale=0.86`; no source PNG is resized or rewritten.
- `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, `runtime_use=true`, `production_asset=true`.
