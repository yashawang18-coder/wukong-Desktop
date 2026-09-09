# Petrified Coin v19 Candidate

This versioned candidate contains eight owner-supplied coin masters: front and
back faces for vivid, flat, faded, and exhausted states. It also contains four
deterministically derived nine-frame front-to-back flip sequences.

The import does not redraw, recolor, crop, or resize visible master artwork.
RGB is cleared only where alpha is exactly zero to avoid fringe during WPF
resampling. Intermediate flip frames are horizontal compressions of the matching
front/back pair using premultiplied-alpha Lanczos resampling. Frame 1 and frame 9
are exact pixel copies of the canonical front and back faces.

The active owner preview remains behind the existing magic PrototypePreview gate.
This package is not production-approved and requires Windows transparent WPF
renderer review before `runtime_approved` or `runtime_use` can change.
