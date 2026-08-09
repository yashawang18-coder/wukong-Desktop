# Phase 1 Code Generation Guide

## First pass: audit and plan only

Read all applicable `AGENTS.md`, root state/decisions, contracts, schemas, generated gaps, asset manifests, tests, reviewed UX, and the pinned Pupu reference. Report what exists; do not infer missing Windows code or runtime readiness.

Produce an implementation audit, Pupu reuse matrix, file-level Phase 1 plan, tests, risks, and blockers. Stop for owner confirmation before large implementation. Work on a focused branch; do not modify or push `main` without explicit authorization.

## Implementation rules

- Domain core is independent of WPF, file paths, concrete model providers, wall clock, and nondeterministic random sources.
- UI, model, scheduler, and menu call the same application service with `BehaviorRequest`.
- Assets load only through a validated contract/registry abstraction.
- Cancellation, safe interruption, fallback, and outcomes are first-class.
- Persisted formats have schema versions, migrations, atomic replacement, and corruption recovery.
- User-facing messages distinguish model-connected, fake/local fallback, and unavailable states.
- Existing canonical behavior IDs come from contracts; do not invent IDs from Chinese labels.
- Keep unfinished/non-approved assets unavailable and explain the exact blocker in trace.

## Prohibited shortcuts

- direct UI/model calls to the animation player;
- arbitrary two-way writes into domain state;
- switch statements mapping behavior IDs to scattered file paths;
- random unrelated animation when eligibility has no candidate;
- copying/translating frames and claiming independent motion phases;
- preview/simulation writing production history or memory;
- silently loading stale local asset packs over embedded versions;
- packaging any Pupu asset, user data, credential, or reference tree;
- claiming Windows real-machine success from Linux/static/cross-build checks.

## Definition of done

Implementation, tests, manifests, trace, root state, decisions, and build report agree. Report verified commands and environment, unverified real-machine work, file changes, migration impact, rollback boundary, and remaining asset gaps.
