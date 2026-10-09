# Behavior Continuity v1

## Fact flow

All owner text, menu actions, autonomous decisions, and magic requests converge on the runtime request path:

```text
input adapter -> normalized intent -> BehaviorRequest -> runtime gate
-> animation request -> lifecycle callback -> PetStateReducer
-> dialogue fact projection -> validated owner-visible text
```

`OwnerDialogue` means an explicit action request typed by the owner. `Dialogue` remains a model/system source and cannot use owner-only food, command, car-ride, or prototype-magic gates.

Before this change, ordinary chat generated model text before attempting an action, initiative text relied on a shallow snapshot, autonomous ticks selected from a legacy flat pool, and completion branches wrote runtime fields directly. The v1 path resolves owner action language first, issues the behavior request, and derives its reply from the accepted/deferred/rejected outcome. Model text is still allowed for non-action conversation, but its state claims are validated before display.

## Runtime facts

`PetAgentState` is the in-process formal state. It contains the stable posture, detailed pose ID, active execution ID, active action, needs, mood, relationship, current Episode, recent outcomes, preferences, and elapsed-time clock. `PetStateReducer` accepts lifecycle events only when their execution ID matches the active execution; preview modes, duplicate callbacks, and stale callbacks cannot settle production state.

Stable idle loops are display states. They are not busy executions and do not repeatedly apply state effects.

## Episode policy

| Episode | Minimum | Preferred | Maximum | Switch margin | Cooldown | Authoritative |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Resting | 45 s | 120 s | 240 s | 0.16 | 45 s | yes |
| Sleeping | 300 s | 600 s | 1200 s | 0.30 | 300 s | yes; compatible sleep presentation holds until approved wake |
| Observing | 20 s | 60 s | 120 s | 0.14 | 30 s | yes |
| Exploring | 20 s | 90 s | 180 s | 0.18 | 60 s | yes |
| Eating | 10 s | 15 s | 40 s | 0.30 | 90 s | lifecycle only |
| Drinking | 10 s | 15 s | 40 s | 0.30 | 90 s | lifecycle only |
| OwnerInteraction | 1 s | 20 s | 120 s | 0.30 | none | lifecycle only |
| MagicActivity | 1 s | 20 s | 180 s | 1.00 | none | lifecycle only |
| VehicleActivity | 10 s | 15 s | 180 s | 0.50 | none | lifecycle only |

Each autonomous tick first advances state by real elapsed time, then evaluates whether the current Episode should continue. A new Episode must pass minimum dwell and beat the current score by its switch margin. During rollout the legacy selector is evaluated only for trace comparison; it cannot submit a second request for an authoritative Episode.

## Episode allowlists

- Resting: approved stable stand/sit/prone idles, approved P2/V3R1 daily lifecycle, `StandToSit`, and `SitToProne`.
- Observing: compatible stable idles and posture preparation, plus `HeadLowerTurnV4` and `FrontProneLickV4` only from their matching prone pose family.
- Exploring: stable stand and approved left/right patrol.
- Sleeping: approved sleep main lifecycle and compatible front sleep breathing only.

Commands, jump, spin, food, drink, magic, and car ride are excluded from autonomous allowlists.

## Approved posture graph

```text
Stand --wk.daily.stand_to_sit--> Sit
Sit   --wk.daily.sit_to_stand--> Stand
Sit   --wk.daily.sit_to_prone--> ProneSide
ProneSide --wk.daily.prone_to_sit--> Sit
ProneCompatible --approved sleep entry--> Sleeping
Stand --approved patrol left/right--> Stand
```

Food and drink use `Prone -> Sit -> Stand` or `Sit -> Stand` before their standing action. Owner commands use their declared transition plan and then briefly hold the exact terminal frame before entering the matching stable idle.

Missing production edges are not synthesized: approved front/side wake routes are used for their matching sleep poses, while no general `ProneFront <-> ProneSide` camera bridge is assumed. After the entry animation, Sleeping stays on a compatible breathing loop or terminal sleep frame for at least five minutes before a bounded natural wake. Owner dialogue and commands may request an earlier wake through the deterministic sleep-interruption policy; an unsafe entry transition is never hard-cut.

## Intent and language mapping

| Owner wording | Intent | Runtime path |
| --- | --- | --- |
| 坐/坐下 | `command.sit` | command planner |
| 卧/趴下 | `command.down` | command planner |
| 手/握手 | `command.paw` | posture-dependent paw command |
| 跳 | `command.jump` | command planner |
| 转圈 | `command.spin` | command planner |
| 吃饭 | `interaction.eat` | approved food route |
| 喝水 | `interaction.drink` | approved water route |
| 睡觉 | `episode.sleep` | compatible prone sleep route |
| 走走/巡视 | `episode.explore` | approved standing patrol route |
| 兜风 | `interaction.car_ride` | owner-only vehicle route |
| 骑扫把/瞬移/石化 | matching magic intent | owner-only magic gate |

Accepted replies use `Preparing` claims and require an active request ID. Current-tense claims must match live Episode/action/posture. Completed claims require a matching completed recent outcome. Invalid model or initiative claims are suppressed and replaced by a deterministic truthful sentence.

## Outcome ownership

Normal non-idle motions are tracked by execution ID and settled through `PetStateReducer`. Reviewed behaviors keep action-specific state effects rather than category defaults. Preview executions restore their isolated snapshot and cannot write production state, relationship, or memory. Desktop effect controllers remain responsible for opacity, coordinates, coins, and temporary visual cleanup.

Formal Agent state is persisted atomically in `WukongData/agent/pet-agent-state.json`. Restart recovery clears active execution and busy flags, advances only a bounded elapsed interval, then restores relationship, needs, recent outcomes, learned preferences, posture, and pose. This prevents a crashed animation from remaining busy while preserving slow relationship and behavior learning.

Relationship and structured long-term behavior memory participate only after hard eligibility. Trust/familiarity adjust social, observe, explore, rest, and command willingness scores; recent outcomes add a small time-decayed component; bounded learned preferences shift close candidates. They cannot enable an unapproved asset, cross an Episode allowlist, or bypass posture/source gates.

Initiative speech uses the same current state, Episode, relationship, and bounded recent experience. It is suppressed while busy, petrified, chatting, stressed, or in quiet hours; repeated topics, unanswered initiatives, and the eight-hour speech budget reduce interruption. Owner commands use deterministic high-cooperation admission with explicit state exceptions instead of random refusal.

## Diagnostics

Trace events include normalized intent, request/correlation ID, source, execution mode, Episode transition, candidate scores and gate reasons, motion start, completion/interruption/failure, final posture/pose, dialogue claim rejection, and safe fallback text.

## Remaining gaps

- Continue Windows observation of the approved wake routes, especially early owner interruption and long sleep holds.
- Add explicit visual bridges between front-prone and side-prone pose families.
- Continue removing unreachable legacy completion branches from `DesktopPetRuntime` after this candidate is accepted; the Normal lifecycle intercepts them now, but compatibility code remains for rollback.
- Replace remaining compatibility outcome profiles with explicit semantic effects as their legacy motions are reviewed; formal settlement already goes through the reducer.
- Run 30-minute Windows observation for Resting and Observing continuity; automated tests do not replace visual review.
