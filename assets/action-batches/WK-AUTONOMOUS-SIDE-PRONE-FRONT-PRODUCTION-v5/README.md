# WK-AUTONOMOUS-SIDE-PRONE-FRONT-PRODUCTION-v5

Thirty-six-frame side-prone forward-observation lifecycle extension, revised from three owner-supplied prone Shiba pose references:

- `bridge-to-front`: 12 frames from the frozen V3R1 side-looking prone anchor to the screen-facing head pose.
- `side-prone-front-calm`: 12-frame calm loop with V3R1 breathing, one slow blink, and one subtle ear twitch.
- `bridge-to-legacy`: the exact byte-reversed bridge so the existing V3R1 exit remains continuous.

The owner photographs guide only the relaxed side-prone anatomy: asymmetrical extended forelegs, hips and rear legs resting to one side, lightly raised chest, and a natural neck turn toward the viewer. They are not copied into or committed with the runtime package. Wukong's identity remains bound to `wukong-current-adult-v1`, and rendering is matched to the approved V3R1 lively material.

The torso, paws, rear legs, and tail continue the corresponding V3R1 side-prone microloop. Only a bounded head/upper-neck region changes; pixels at `x >= 560` and `y >= 760` remain byte-identical to the corresponding V3R1 body frame. The V3R1 package is not edited, mirrored, redrawn, recolored, rescaled, blurred, or sharpened.

The owner requested runtime enablement on 2026-08-26, then supplied new pose references for this revision. The revised boards therefore remain fail closed pending owner visual review and the branch's Windows WPF decoder/build/test workflow. Promotion is a separate metadata change after both pieces of evidence exist.
