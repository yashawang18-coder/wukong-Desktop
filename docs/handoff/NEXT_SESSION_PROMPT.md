# Next Session Prompt

Use this first in Plan mode and fill the references:

```text
Continue Wukong Desktop from the real repository state.

Repository: https://github.com/yashawang18-coder/wukong-Desktop
Target base commit: [main SHA]
Reviewed UX artifact: [path/version]
Pinned Pupu reference: [path or repository/ref/SHA]
Current target: audit and plan Phase 1 backend/behavior runtime; do not implement yet.

First:
1. Read all applicable AGENTS.md, root CURRENT_STATE.md and DECISIONS.md, docs/handoff/, contracts, schemas, generated P0 gaps, relevant asset manifests, and tests.
2. Inspect the actual tree, branch/PR state, build files, CI, runtime registry, and supplied Pupu source. Never assume a module exists when it is not found.
3. Classify Pupu modules as selectively adaptable, reference-only/rewrite, or prohibited.
4. Report every P0 asset as approved-keyframes, runtime-candidate, runtime-approved, unavailable, or partial using manifest evidence. Do not confuse preview approval with runtime approval.
5. Design only this flow:
   InputEvent -> Intent -> BehaviorRequest -> Eligibility -> Arbitration -> Execution -> AnimationLifecycle -> Outcome -> RuntimeState -> Event/Memory -> Trace.
6. Produce CURRENT_IMPLEMENTATION_AUDIT.md, PUPU_REUSE_MATRIX.md, PHASE1_IMPLEMENTATION_PLAN.md, PHASE1_TEST_PLAN.md, and BLOCKERS_AND_QUESTIONS.md, then stop for confirmation.

Boundaries:
- UI/menu/model/scheduler submit BehaviorRequest and never directly play animation or mutate state.
- Model behavior intent is optional and must pass normal eligibility/arbitration.
- Animation is manifest-driven with intro/loop/exit/interrupt_exit and declared fallback.
- Preview, simulation, and developer-forced runs do not write real state or memory.
- Do not generate missing frames, enable non-runtime-approved assets, migrate Pupu behavior/assets/data, or expand to P1-P4.
- Work on a focused branch. Do not commit, push, merge, or modify main until explicitly authorized.
- Distinguish repository tests, cross-build, CI, and Windows real-machine validation.
```
