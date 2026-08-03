# Project Requirements

## Goal

Build Wukong as a maintainable desktop pet whose visual identity, animation assets, autonomous behavior, user interactions, and Windows release process can evolve independently without losing traceability.

## Product principles

- Character identity must remain stable across actions and directions.
- Motion should read as continuous animation, not a slideshow of independently regenerated pictures.
- Autonomous behavior should feel coherent and interruptible, with cooldowns and repetition control.
- Direct interaction should produce predictable feedback and must not leave the pet in an invalid state.
- The control/UI layer should expose actual state and diagnostics without becoming the behavior engine.
- Every release must be reproducible from versioned source, manifests, and build configuration.

## Architecture boundaries

Keep these concerns separate:

1. **Identity profile**: appearance anchors and anti-drift rules.
2. **Action assets**: keyframes, runtime frames, previews, metadata, and review state.
3. **Behavior core**: state, scoring, transitions, cooldowns, interruption, and memory.
4. **Platform presentation**: transparent window, input routing, rendering, control panel, and OS integration.
5. **Persistence**: settings, relationship/runtime memory, migration, and privacy.
6. **Build/release**: tests, packaging, installer, signing, CI, and update delivery.

Dependencies should point inward through interfaces: UI and platform code may call the behavior core, while the behavior core must not depend on window classes or concrete asset file paths.

## Acceptance rules

- A feature is complete only when implementation, relevant tests, diagnostics, and documentation agree.
- A visual action is complete only when it reaches `runtime-approved` and is registered through metadata rather than hard-coded path scattering.
- A release is complete only after installation and launch are verified on the target Windows architecture.

