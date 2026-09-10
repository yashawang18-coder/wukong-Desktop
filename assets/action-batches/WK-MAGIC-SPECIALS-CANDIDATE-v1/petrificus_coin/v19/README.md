# Petrified Coin v19 Candidate

This versioned candidate contains four owner-supplied refined front masters and
four preserved back masters for vivid, flat, faded, and exhausted states. It
also contains four deterministically derived nine-frame front-to-back flips.

The refined 1254 px fronts are normalized as complete images to the established
1024 px runtime canvas and shared visible bounds. The four 1024 px back masters
retain their visible artwork; only hidden RGB under zero alpha is cleared. No
face is locally patched, redrawn, recolored, sharpened, or blurred. Intermediate flip frames
are premultiplied-alpha horizontal compressions of the matching face pair.
Frame 1 and frame 9 exactly match the normalized front and canonical back.

The active owner preview remains behind the existing magic PrototypePreview gate.
This package is not production-approved and requires Windows transparent WPF
renderer review before `runtime_approved` or `runtime_use` can change.
