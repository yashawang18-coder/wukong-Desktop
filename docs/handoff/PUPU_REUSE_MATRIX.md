# Pupu to Wukong Reuse Matrix

Provide a pinned complete Pupu source snapshot or repository/ref so dependencies can be understood. Keep it under `reference/pupu/` or outside the build graph and treat it as read-only.

| Pupu area | Wukong treatment |
|---|---|
| Transparent/topmost WPF host, DPI, multi-monitor bounds, scaling, window position | Selectively adapt after audit |
| Raw mouse input: click, double-click, hold, drag, rapid tap, wheel, menu | Reuse capture concepts; emit semantic input events |
| Settings, credential protection, logging, crash recovery, tests, installer/update scaffolding | Selectively adapt with Wukong names and privacy rules |
| Six-tab panel shell, album file access, provider settings | Reuse UX/infrastructure concepts; connect to Wukong services |
| Behavior selector, autonomous scheduler, interaction response | Reference only; implement Wukong contracts |
| Animation player and hard-coded mappings | Rewrite as manifest-driven lifecycle orchestrator |
| State mutation, personality learning, model action trigger, memory feedback | Rewrite with outcome/event ownership and safety gates |
| Pupu behavior IDs, cat assets/atlases, user settings, memory, album, secrets, binaries | Prohibited |

The build and release pipeline must prove that `reference/pupu/` and prohibited material are excluded from Wukong outputs. Do not bulk-copy namespaces and then rename them; migrate the smallest independently testable infrastructure slice at a time.
