# WK Runtime Lifecycle Microloops Candidate v2

Developer candidate profile only. Not production runtime, not command assets, not right-click user actions.

Gate state: `runtime_approved=false`, `runtime_use=false`, `runtime_validation=pending_renderer_qa`.

Lifecycle mapping: intro is the 14-state forward sequence; loop is prone-idle; exit is prone-to-sit plus sit-to-stand; interrupt_exit returns through the nearest stable anchor; fallback is stable stand frame 01.

Microloop timings are copied from the recovered v2.1 package. Intro and exit timings are candidate runtime initial values because the lively package states timing has not been validated in runtime.

## P3 activation

Windows manual transparent-renderer acceptance passed on 2026-08-15. The batch is promoted to `runtime_validation=passed_windows_renderer_qa`, `runtime_approved=true`, and `runtime_use=true` for the autonomous lifecycle profile only. It remains outside right-click menus, command/training actions, magic, coins, and unrelated UI work.
