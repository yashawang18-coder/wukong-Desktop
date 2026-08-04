# Runtime generation V3

## Owner feedback addressed

- V1 rejected: ear motion was too pronounced and blink repeated every 1.5 seconds.
- V2 rejected: breathing motion existed but was visually unreadable at normal preview size.
- V3 target: readable calm breathing, stable ears/head/paws, and an occasional independently scheduled blink.

## Deterministic construction

- Base loop: 24 frames, 8 FPS, 3000 ms.
- Breathing interpolation: approved frame 001 to approved frame 002, cosine easing, full approved amplitude at frame 013.
- Stable region: the approved frame 001 head, eyes, and ears are composited over the breathing blend.
- Ear-pose frame 003 is excluded from the base loop.
- Blink variant: four non-looping frames at 8 FPS, using approved open-eye frame 001 and approved closed-eye frame 004 only inside a feathered eyes-only mask.
- Runtime scheduling proposal: randomized 15-30 second interval; not integrated because application source is unavailable.
- Review preview: one blink in 12 seconds so the owner can inspect it without waiting 30 seconds.

## Rejected image-generation candidate

An image-edited half-blink candidate was rejected because it changed body scale, placement, paws, and silhouette. It is not included in the action package or used as a reference.
