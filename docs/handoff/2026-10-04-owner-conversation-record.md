# Owner Conversation Record - 2026-10-04

## Status Of This Record

This is a faithful structured reconstruction of core owner instructions, approvals and unresolved concerns from the prior Codex task. It is not a byte-for-byte export of the chat UI transcript, which is unavailable to the local coding agent. For current facts, inspect Git, manifests, `CURRENT_STATE.md`, `DECISIONS.md`, tests and the delivered EXE.

## Repeated Non-Negotiable Rules

- Preserve user changes. Never use `reset --hard`, destructive checkout, unapproved stash, force push, rebase or amend.
- Do not modify `main` without direct authorization. Do not merge or create a PR unless explicitly authorized for that operation.
- Do not modify approved source PNGs, regenerate art, crop, recolor, interpolate or resample unless a new versioned art workflow is explicitly requested.
- Keep visual approval distinct from Windows renderer/runtime approval.
- Candidate, developer-preview and production routes must not silently cross approval boundaries.
- Never use missing assets to fall back to unrelated old standard-Shiba or red material.
- Preserve raw source, SHA inventories, manifests and rejected material for audit unless physical deletion is explicitly requested.
- `.asset-staging/` is authorized local input and remains untracked.

## Wukong Identity And Dialogue

Wukong is an adult Chinese Shiba Inu companion in Nanjing Jiangning. He calls the owner `老爸`. He is affectionate, playful, greedy, attached, mischievous and a little possessive, but trusted and not aggressive. He is a pet, not an AI assistant, code helper, secretary or work agent.

- First person, short simple Chinese, normally about ten characters and never long explanatory replies.
- Natural pet words such as `饭饭`, `玩具`, `绳绳` and `胸背` are acceptable.
- Never call himself an AI/model/system or expose prompt engineering.
- For work requests, gently refuse in character rather than doing professional work.
- Dialogue must match formal posture/action/mood. Never say he is prone while visibly standing.

## Behavior Design Decisions

1. A deterministic local engine owns action selection. The LLM writes language from already-admitted state and intent; it never selects visual files.
2. Owner commands are normally cooperative, but pose safety, availability, busy/safe interruption, energy, stress and repetition can produce Accepted, Deferred or Rejected with a real reason. `Stop` always works.
3. Magic is an owner-initiated special performance. It may preempt ordinary behavior only safely and is never autonomous/model/command driven.
4. Car ride is owner/manual only. It must not enter AutonomousTick, dialogue, model, command or startup autoplay.
5. Daily life favors walking, front-prone resting and sleep instead of frequent standing. Jump and spin are command/developer-preview only.
6. Daily selection uses minimum dwell, episode continuity, cooldown and repeat penalties. Stable idle is a presentation, not a repeated outcome.
7. Relationship, album and confirmed dialogue memory become small bounded score modifiers only after hard eligibility gates.
8. Initiative speech is low-frequency and state-aware. It must not become a notification stream.

## UX Decisions

- Six primary areas: Owner, Profile, Album, Model, Assets and Developer.
- Owner UI uses understandable Chinese states/actions. Technical IDs and frame/lifecycle details belong in Developer mode.
- Asset tabs match the right-click menu: 基础动作、玩一下、吃一下、口令、魔法特辑、节日特辑（待解锁）.
- Asset cards distinguish `查看动画` and `让悟空执行`; owner inspection execution uses renderer/lifecycle but does not teach personality or memory.
- Deprecated assets are gray and visible only through an expired filter. They are never executable or fallback candidates.
- Profile and Model need balanced field sizes and native editing. Personality is user-adjustable via persistent sliders.
- Album supports binding/unbinding and deleting independent subalbums. Unbound media must not linger as an undeletable gray card.
- Chat opens through the pet interaction sensor, stays close to the pet, supports Enter to send and restores input focus after each turn. It must not collapse after a successful reply.
- Developer mode shows mechanism docs, gates, score traces, action statistics and current runtime facts without bypassing production policy.

## Asset Direction

- Preserve one adult Wukong identity: natural light malt-gold coat, believable body proportions, stable facial identity, coat texture, anchor and baseline.
- Visible-alpha bounds determine scale. Effective scale is user scale times declared action-local scale and survives action/magic/car/coin changes.
- Do not repair motion through head/body splicing, crossfade ghosting, overlays, interpolated frames or per-frame auto-fit. Version new art or fail closed.
- Mirror only complete non-directional pet scenes. Never mirror magic, native car directions or front-only expressions. Orientation stays fixed inside a request.
- Walking needs believable diagonal gait and whole-window translation. Straight travel is faster than turns; avoid same-side gait and coat/material jumps.
- Sleep camera families must not hard-cut. Never reverse sleep-entry frames to fake waking.
- Backward-looking side-prone imagery is visually disfavored. Its rejected first transition frame was removed from effective rise/prone-to-sit timelines; original source evidence remains.

## Retained Action Semantics

- Commands: `坐`, `卧`, `手`, `跳`, `转圈`, `吃`; no `停` command menu item.
- Paw/eat choose seated or prone branches according to compatible current posture and preserve declared end posture.
- Food/water design remains wanted even when a candidate asset is rejected. `吃一下` includes `喝水` and `吃饭`; activation depends on the material's gate.
- Apparate slowly disappears, stays gone 5-10 seconds and slowly returns at a valid position using its own magic exit frames. No ordinary frame may appear during the effect.
- Coin behavior needs a complete flip with intermediate states, stable scale and responsive double-click flip.
- Broom flight needs real work-area rise, arc and landing, not a larger pet image.

## Approval Notes

- `visual_approved=true` only represents its stated scope. It never alone grants runtime approval.
- Windows transparent WPF playback and owner visual review are separate gates.
- Car ride v8 was approved for manual Normal owner execution only; it remains excluded from autonomous, dialogue, model and command sources.
- Historical red/standard-Shiba batches may be motion references but are not production identity/color references.
- The owner values early high-quality prone, command and magic identity. Lower-quality later sleep/walk art should be versioned/rebuilt or retired, not cosmetically patched.

## Recent Work And Next Tasks

- Agent v2 added/extended one formal `PetAgentState`, elapsed-time evolution, `PetStateReducer`, capability catalog, participation policies, execution IDs, deterministic traces and staged Episodes.
- Relationship/memory, initiative speech and command willingness are bounded, testable scoring inputs. Improve quality/frequency without letting them bypass gates.
- Portable profile/prompt configuration and optional conversation history travel with the EXE, but private user data must not be committed.
- Current dashboard work adds category statistics, line/bar charts and a filterable table. See `2026-10-04-codex-account-transfer.md`.
- Requested next design topic: mouse awareness. It should convert proximity/dwell/movement into input events, then use posture/busy/priority gates for low-frequency compatible motion or brief language. It must not cause continuous pointer chasing or bypass `BehaviorRequest`.
- The full Desktop console runner has a cross-STA WPF `Application` lifetime problem. Repair it as a focused testing task before treating its aggregate as a release gate.

## New-Account Starter Prompt

```text
Continue Wukong Desktop from its actual Git state. Read AGENTS.md,
CURRENT_STATE.md, DECISIONS.md, ASSET_STRUCTURE.md,
docs/handoff/2026-10-04-codex-account-transfer.md and
docs/handoff/2026-10-04-owner-conversation-record.md before modifying code.

Preserve all user files and .asset-staging/. Do not reset, rebase, force-push,
modify main, alter PNGs or promote an asset based only on visual approval. Keep
every action in the BehaviorRequest -> eligibility -> arbitration -> lifecycle
-> outcome -> PetStateReducer chain. Inspect current manifests, tests and EXE
behavior before making assumptions.
```
