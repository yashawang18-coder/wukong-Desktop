# Prompt record

The built-in image editor was used to produce six isolated head/upper-neck sources from one shared forward-facing master: four turn angles (`52°`, `35°`, `15°`, and front), one closed-eye blink, and one subtle ear-twitch variant.

Reference roles:

- the frozen V3R1 side-looking frame fixes the body, start anchor, canvas, scale, lighting, and lively rendering;
- `wukong-current-adult-v1/identity-board.png` fixes Wukong's face, adult compact build, short muzzle, narrow attentive eyes, ears, and warm-malt/cream markings;
- the three owner-supplied photographs guide only side-prone anatomy and the natural head/neck turn; the photographs are not repository or runtime assets.

Common constraints:

- transparent-runtime output is produced only after chroma-key extraction;
- flat `#00FF00` source background;
- calm closed mouth, adult Shiba identity, light-malt-gold coat;
- no body, legs, collar, leash, harness, text, watermark, cartoon styling, or red oversaturation;
- calm closed-mouth observation; one low-frequency slow blink and one very small ear twitch;
- head size and neck connection must remain stable across all turn stages.

Post-processing is deterministic: chroma extraction, green-spill suppression, warm-malt grading, 310×380 normalization, curved lower-neck alpha feathering, and placement over the corresponding frozen V3R1 body frames. The forward loop reuses the same front master with V3R1 breathing frames, a slow blink, and a subtle ear twitch. The reverse bridge is an exact byte reversal of the forward bridge.

Original generated-output and owner-reference SHA-256 values are recorded in `manifest.json`; only normalized RGBA cutouts and final runtime/review frames are committed.
