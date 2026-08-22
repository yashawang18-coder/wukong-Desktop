# Autonomous behavior and interaction roadmap

Status date: 2026-08-23. Branch scope: `agent/personality-state-behavior-mock-v1`.

## Implemented foundation

The desktop now has one `PetRuntimeState`, one posture-compatible autonomous scheduler, a state-driven initiative-speech decision, and one interaction decision service. Decisions distinguish an input being emotionally acknowledged from an animation being allowed. A missing or locked visual asset therefore produces a bounded state update plus `Deferred`, not a fallback red animation or a false `Accepted` playback.

The decision order is:

1. Classify pointer input as touch, stroke, rapid tap, drag, or double-click.
2. Apply hard context gates: petrified state, stable-idle/interruptibility, and pose.
3. Evaluate stress, temperament sensitivity, touch/initiative acceptance, and cooldown.
4. Apply bounded immediate input effects such as rapid-tap stress or touch comfort.
5. Resolve only a runtime-approved behavior ID; otherwise return a reason-coded defer/reject result.
6. Apply behavior outcome changes only after renderer completion.

## Candidate batch produced in this change

| Proposed daily behavior | Frames | Approved pixel source | Current gate | Review question |
|---|---:|---|---|---|
| `wk.daily.stand_to_sit` | 10 | v4 Sit | Candidate | Does a command-style sit feel self-motivated without an owner cue? |
| `wk.daily.sit_to_prone` | 12 | v4 Down | Candidate | Is pacing calm enough for routine settling? |
| `wk.daily.prone_to_sit` | 4 | P2 lifecycle exit | Candidate | Are four frames sufficient at desktop scale? |
| `wk.daily.sit_to_stand` | 5 | P2 lifecycle exit | Candidate | Is the rise smooth at variable frame timing? |
| `wk.daily.playful_hop` | 12 | v4 Jump | Candidate | Should autonomous use be rarer and lower-energy than the command? |
| `wk.daily.playful_spin` | 16 | v4 Spin | Candidate | Does a full spin look spontaneous or trained on command? |

Approval sequence: owner semantic review board/GIF, Windows transparent-renderer playback, scale/pivot/alpha/interruption checks, explicit runtime approval, then and only then autonomous-pool registration with state thresholds and cooldowns.

## Next visual assets, in priority order

| Priority | Asset | Suggested frames | Why it matters | Reuse rule |
|---:|---|---:|---|---|
| P0 | Prone touch v4.1 Windows QA | Existing 70 | Unlocks the most common owner interaction without using expired visuals | Validate existing batch; do not redraw or promote by code alone |
| P0 | Light-malt-gold stroke/enjoy response | 12–18 | Current stroke reference is expired and cannot be shown | Motion rhythm may be referenced; pixels must be new/approved Wukong |
| P0 | Light-malt-gold rapid-tap/startle-settle | 8–12 | Lets repeated clicks produce visible boundaries and recovery | Avoid aggression; response strength follows stress/sensitivity |
| P1 | Stand/sit/prone ear-twitch or blink variants | 5–8 each | Repetition is most visible inside long stable idles | Preserve exact posture anchor and use long irregular holds |
| P1 | Notice/sniff/short head-turn | 8–12 | Supports curiosity without a full lifecycle transition | May reuse P2 motion ideas, never expired pixels |
| P1 | Prone stretch and stand stretch | 10–14 each | Adds natural low-frequency maintenance behavior | New light-malt-gold production frames preferred |
| P2 | Yawn, body shake, resettle | 10–16 each | Makes energy/comfort state readable | Keep scale and ground baseline fixed |
| P2 | Attention request without command gesture | 10–14 | Gives social need a visible, nonverbal outlet | Do not repurpose paw/handshake without owner semantic approval |
| P2 | Drag-release orientation/settle | 6–10 | Makes relocation feel grounded after the pointer releases | Drag itself remains direct window movement |

## State and relationship policy

- Temperament is owner-controlled baseline and is not rewritten by ordinary interaction.
- Runtime state changes immediately only for direct, bounded input effects. Large behavior effects commit at animation completion, not selection time.
- Relationship touch acceptance and initiative acceptance gate contact and spontaneous language separately.
- Repeated unwanted touch raises stress; accepted gentle touch may reduce social need and raise comfort. No single click should produce large or permanent personality change.
- Initiative speech remains local-template based until explicit conversation. A model may propose an intent only through the normal request/gate path and can never select asset files or force playback.

## Acceptance tests still required

- Real Windows transparent WPF renderer QA for prone touch and every newly promoted daily batch.
- Owner review of autonomous semantics: motion can be visually correct but still look like obedience to an invisible command.
- Long-duration soak checking repetition, cooldown, quiet hours, drag/touch conflicts, and speech frequency.
- Dark/light desktop background checks for alpha edges and no reappearance of red/standard-Shiba visuals.
- Telemetry review of reason codes (`Deferred`/`Rejected`) before tuning thresholds; thresholds must not be changed just to increase animation count.
