# Repair provenance

The defective v9 frames were used only to identify intended road-gaze pose progression. Matching approved v8 native-direction frames supplied Wukong/car identity, body placement, and the six wheel phases.

Five coherent full Wukong-in-car pose masters were generated independently for each native direction on a flat chroma background. Chroma was removed, each master was resized to 1024 x 1024, and the visible Wukong subject was scaled to 86% around a fixed direction-specific anchor. A deterministic feathered head/body boundary then restored the exact matching v8 car, wheels, paws, harness, and lower body for every frame phase.

The generated working masters and intermediate masks are build-time working evidence only and are not runtime assets. Runtime output contains only the 36 final transparent PNG frames plus review derivatives. No v9 PNG was overwritten.
