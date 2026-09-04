# Behavior Agent Mock v1

> Runtime integration update (2026-08-23): this engine remains available for deterministic previews and owner-command planning, but it no longer owns a parallel autonomous execution path. Desktop autonomous behavior uses the single approved lifecycle scheduler and shared `PetRuntimeState`. Planned command outcomes are committed only after renderer completion.

## Scope

This is a local, testable mock layer for Wukong personality, realtime state, mood, owner commands, autonomous scoring, and dialogue context. It does not promote any command mock art to production runtime.

The developer panel switch `EnableBehaviorAgentMock` exposes diagnostics and decision previews. Formal runtime gates remain authoritative regardless of the switch, and autonomous playback always uses the unified approved lifecycle scheduler.

## Data Flow

```text
InputEvent / owner command / autonomous tick
-> BehaviorDecisionContext
-> hard constraints
-> utility scoring
-> PetDecision
-> BehaviorRequest / desktop runtime request
-> runtime asset gate
-> AnimationLifecycle
-> PetStateUpdate
-> DialogueContextSnapshot
```

The local decision engine chooses actions. A model may receive a dialogue context snapshot, but it must not select asset files, mutate pet state, set PrototypePreview, or bypass runtime gates.

## Personality, State, And Mood

`TemperamentProfile` is the slow baseline:

- Activity: increases lively actions when energy is high.
- Attachment: increases owner-oriented behavior when social need is high.
- Sensitivity: increases stress response to repeated clicks and makes dialogue more cautious under stress.
- Independence: favors maintaining posture and self-directed idle behavior.
- Mischief: increases playful jump/spin preference when stress is low.

`PetRuntimeState` is the fast-changing state:

- CurrentPosture: Stand, Sit, or Prone.
- Energy, Hunger, SocialNeed, Boredom, Stress, MoodValence, Arousal.
- LastInteractionAt, LastActionId, RepeatedActionCount.
- ActiveActionId and IsBusy for action mutual exclusion.

Mood is derived from runtime state and temperament. It affects expression labels, speed/response intensity hooks, and dialogue tone. It does not change asset identity.

## Scoring

Autonomous choices use hard constraints first, then utility scoring:

```text
finalScore = baseWeight
  + temperament
  + runtime_state
  + relationship
  + context
  - cooldown_penalty
  - repetition_penalty
  - transition_cost
  + bounded_randomness
```

The random term is bounded and seeded. Same state, same clock, same seed, and same candidates produce the same score table and selection.

Hard constraints can eliminate candidates for busy state, disabled initiative, cooldown, low energy, or high stress. Randomness never bypasses these constraints.

## Owner Commands And Posture Matrix

Owner commands are deterministic and have priority over autonomous decisions, but still produce posture-aware plans.

| Command | Start posture | Selected branch | End posture |
| --- | --- | --- | --- |
| Sit | Stand | `wk.command.sit` | Sit |
| Sit | Sit | `wk.command.sit` light response | Sit |
| Sit | Prone | `wk.mock.transition.prone_to_sit` + sit response | Sit |
| Down | Stand | `wk.command.sit` + `wk.command.lie_down` | Prone |
| Down | Sit | `wk.command.lie_down` | Prone |
| Down | Prone | `wk.command.lie_down` light response | Prone |
| Paw | Sit | `wk.command.paw_sit` | Sit |
| Paw | Prone | `wk.command.paw_prone` | Prone |
| Paw | Stand | `wk.command.sit` + `wk.command.paw_sit` | Sit |
| Jump | Stand | `wk.command.jump` | Stand |
| Jump | Sit | `wk.mock.transition.sit_to_stand` + jump | Stand |
| Jump | Prone | prone-to-sit + sit-to-stand + jump | Stand |
| Spin | Stand | `wk.command.spin` | Stand |
| Spin | Sit | sit-to-stand + spin | Stand |
| Spin | Prone | prone-to-sit + sit-to-stand + spin | Stand |
| Eat | Sit | `wk.command.eat_sit` | Sit |
| Eat | Prone | `wk.command.eat_prone` | Prone |
| Eat | Stand | sit + eat-sit | Sit |

Completed actions keep their declared `EndPosture`. They do not automatically reset to Stand.

## Command Asset Status

The current command frames live in:

```text
assets/action-mocks/WK-COMMAND-PRODUCTION-CANDIDATES-v4/
```

They replace the earlier rough cartoon mock frames. The owner has approved this batch for explicit manual command playback from the desktop context menu and the control panel command asset page.

Current state:

```text
motion_design_approved=true
production_asset=true
visual_approved=true
runtime_approved=true
runtime_use=true
prototype_use=false
asset_stage=runtime_approved_owner_command
```

The approval scope is narrow: only explicit owner command entry points may run them. They are still forbidden for autonomous behavior, dialogue/model routing, startup autoplay, and unrelated interaction paths.

The older `WK-COMMAND-ACTION-CANDIDATES-v3` batch remains visible as an expired motion reference. It is not deleted, not runtime-enabled, and not selected by the owner command execution path.

## Dialogue Linkage

`DialogueContextSnapshot` exposes:

- personality summary
- current mood and energy/stress/social bands
- current posture and action
- last owner event
- relationship summary
- dialogue tone, initiative level, and response length
- selected behavior intent
- forbidden claims

Local fallback text can use this snapshot without a network model. A model must not claim posture, action, approval state, or asset paths that are not in the selected decision.

## Cooldown, Repetition, And Hysteresis

The first version includes explicit cooldown and repetition penalty components. Busy non-interruptible actions block autonomous decisions. This prevents autonomous behavior from interrupting owner-command execution.

The current hysteresis is intentionally minimal: action switching cost and minimum autonomous interval are used to avoid twitchy posture changes. More persistent mood-band hysteresis can be added after real interaction telemetry exists.

## Current Missing Production Assets

The following production transitions are still represented by mock placeholders:

- prone -> sit
- sit -> stand
- stand -> sit when used as a command mock
- sit -> prone when used as a command mock

Future production replacement should use 1024x1024 RGBA transparent frames, consistent Wukong identity, Wukong Indoor Natural Coat v2 color, stable visible alpha bounds, stable foot baseline, and manifest SHA records.
