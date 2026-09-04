# Wukong command — sit production candidate v2

This package combines the approved sit motion with the approved native-fur identity master. It is an owner-review candidate, not a runtime-approved asset.

## Contents

- `frames/frame-001.png` through `frames/frame-010.png`: 1024x1024 RGBA production-candidate frames.
- `wukong-sit-production-preview-v2.gif`: timing preview using the approved mock GIF's duration profile.
- `wukong-sit-production-review-v2.png`: all frames on light and dark backgrounds.
- `normalization-report.json`: per-frame scale measurements and source mapping.
- `references/`: approved motion, identity/color/fur master, and direct native-fur stand anchor.

## Locked rules

- Identity, light malt-gold/cream color, body mass, and fur quality come from `identity-color-fur-master.png`.
- The original approved GIF supplies the ten-frame action order and timing.
- Real-video samples supply the non-standard joint timing for frames 2–9: hindquarter descent, uneven paw contact, and natural settling.
- Gaze is redirected toward the viewer/desktop rather than copied from the owner's elevated position in the video.
- Every PNG uses genuine alpha transparency, a 1024x1024 canvas, and ground baseline `y=900`.
- Anatomical scale is normalized by contiguous head width (`223.5 px` target); the raised tail is excluded from the measurement.
- Orange fur is selectively calibrated to the master in LAB space. Cream markings, eyes, nose, mouth, paw pads, and claws are not recolored.
- Checker/white background pixels are removed and feathered edge RGB is decontaminated with the nearest solid fur color.

## Frame semantics

1. Approved direct-master standing anchor.
2. Very early weight shift; mostly standing.
3. Early pelvis descent.
4. Mid descent with bent hocks.
5. Late descent with asymmetric forepaw support.
6. First low seated contact with an outward forepaw.
7. Body settling and chest rising.
8. More upright seated posture.
9. Near-final relaxed sit.
10. Final-state hold using frame 9, preserving timing without introducing an identity jump.

## Status

- `production_candidate=true`
- `owner_visual_approved=false`
- `production_asset=false`
- `runtime_use=false`

Owner review should focus on frame-to-frame identity consistency, head scale, pelvis-height progression, front-leg anatomy, fur texture at 100% zoom, transparent edges on dark backgrounds, and whether the final seated state feels sufficiently lively.

## Generation method

The built-in image-generation workflow used three roles per generated frame: real-video pose reference, identity/color/fur master, and approved native-fur standing anchor. Prompts locked the exact motion phase, redirected gaze toward the viewer, removed the collar/environment, required genuine transparency, and prohibited standardized symmetric sitting, thin-body drift, dark-red color, and curly/engraved/plastic fur. Deterministic processing then performed alpha extraction, edge decontamination, LAB color calibration, anatomical scale normalization, baseline placement, review-board assembly, and GIF encoding.
