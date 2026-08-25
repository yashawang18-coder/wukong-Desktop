# Wukong lifecycle V3R1 and forward-prone V4 review

This portable Windows build contains two independent owner-QA candidate profiles.
Neither profile is runtime-approved, available to Normal requests, or enabled in
the autonomous pool.

## Candidate profiles

### V3R1 recovered lifecycle

- Review the recovered intro from stable stand to the historical side-prone pose.
- Review the historical side-prone idle only with the V3R1 intro and exit.
- Review the exit from historical side-prone back to stable stand.
- Review the stand and sit microloops independently.
- V3R1 was reconstructed from the immutable V2 source. It is not a byte-for-byte
  recovery of the unavailable transient V3 package.

### V4 forward-prone

- Review the calm forward-prone loop independently.
- Review the forward-prone lick as one microevent, not a continuous loop.
- The lick must start and finish on the exact stable forward-prone anchor.
- The lick is eligible only from `Prone` and retains a proposed 45-120 second
  autonomous cooldown, but its autonomous binding is disabled in this build.

There is no approved side-prone to forward-prone bridge. Do not treat a manual
switch between these profiles as a valid lifecycle transition.

## How to review

1. Run `Wukong.Desktop.exe` from this directory.
2. Open the control panel and enable the existing developer diagnostics session.
3. Open the developer page and find **基础动作候审**.
4. Verify that exactly seven cards are present: V3R1 intro, exit, stand idle, sit
   idle, historical side-prone idle, V4 forward-prone calm, and V4 forward-prone
   lick.
5. Use **面板预览** to inspect frames against the light and dark preview
   backgrounds.
6. Use **桌面候审** to exercise the real WPF animation path.
7. Stop each looping candidate before starting another candidate.

## Review checklist

- Identity, coat, body scale, alpha edges, paws, and floor baseline remain stable.
- Intro and exit preserve the historical side-prone orientation.
- V4 stays forward-facing and is never hard-spliced onto V3R1.
- V4 calm loop has a clean first/last seam.
- V4 lick plays once, returns to the stable anchor, and does not repeat by itself.
- No expired red/standard asset contributes a runtime pixel or fallback frame.
- Existing command, magic, car ride, autonomous daily, and V2 lifecycle tabs remain.

## Gate state

Both profiles remain:

```text
asset_stage=production_candidate_owner_qa_pending
visual_approved=false
runtime_validation=pending_windows_renderer_qa
runtime_approved=false
runtime_use=false
production_asset=false
autonomous_binding_enabled=false
```

Only an explicit later owner decision may change these fields.
