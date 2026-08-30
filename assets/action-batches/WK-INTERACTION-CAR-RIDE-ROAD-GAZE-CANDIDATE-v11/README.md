# WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v11

Versioned owner-review repair for the optional native left/right road-gaze extension to approved car ride v8. The v8, v9, and v10 PNG files remain byte-unchanged.

Owner review rejected v10 because its generated head poses used inconsistent vertical anchors, making the head move up and down during playback. v11 preserves the v10 pose art and applies only deterministic vertical registration above a fixed neck root. The right sequence head-top line is y=296 in all 18 frames; the left sequence remains within y=296..299. All pixels at y >= 650 still match the corresponding approved v8 direction and wheel slot.

Each native direction contains 18 ordered 1024 x 1024 RGBA frames. No horizontal mirror, crossfade, motion blur, runtime interpolation, or whole-frame redraw is used. The four light/dark contact sheets and four GIFs are review derivatives only.

Owner visual review rejected v11 because the head-only rebuild/composite strategy still produced visibly unstable head motion. v11 is preserved as immutable failure evidence and is superseded by the complete-scene v12 candidate. It is unavailable to Normal and DeveloperPreview playback.

Current gates: visual_approved=false, runtime_validation=failed_owner_visual_qa, runtime_approved=false, runtime_use=false, prototype_use=false, and production_asset=false.
