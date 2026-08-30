# WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v10

Versioned repair candidate for the optional left/right road-gaze extension to approved car ride v8. The v8 package and every v9 PNG remain byte-unchanged.

Each native direction contains 18 ordered 1024 x 1024 RGBA frames. Frames 3-15 use rebuilt, anatomically continuous Wukong head-and-neck poses. Every output frame is composited onto its matching approved v8 car frame, preserving the declared six-slot wheel sequence instead of reusing one whole-frame image under unrelated phase labels. The approved v8 source has three unique rendered wheel images across those six slots; v10 preserves that source truth. No horizontal mirror, crossfade, motion blur, runtime interpolation, or partial head overlay is used.

The four light/dark review boards and four GIFs are review derivatives only. This package remains fail closed for Normal runtime. It is available only in an explicitly marked local DeveloperPreview build until owner visual QA and Windows transparent-renderer QA are both recorded.

Current gates: `visual_approved=false`, `runtime_validation=pending_owner_windows_renderer_qa`, `runtime_approved=false`, `runtime_use=false`, `prototype_use=true`, and `production_asset=false`.

Owner review on 2026-08-30 rejected v10 because the generated head poses used inconsistent vertical anchors and visibly moved up and down. v10 is retained as failed review evidence and is superseded by v11; all playback gates are closed.
