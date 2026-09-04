# WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v12

Rejected visual-review evidence for the optional left/right road-gaze extension to approved car ride v8.

v9, v10, and v11 attempted to preserve the approved v8 vehicle while rebuilding only the head/neck region. Owner review rejected that strategy because the composite boundary and vertical head motion remained visible. v12 does not reuse a head patch. Every source master is a complete generated scene containing Wukong, the connected neck and body, red harness, steering wheel, full turquoise car, and both wheels.

The eight source masters are normalized only as complete images: uniform whole-frame scale, horizontal centering, and a shared wheel baseline at y=900. No head, neck, body, car, or wheel region is independently translated, scaled, warped, or pasted.

Each native direction contains 18 ordered 1024 x 1024 RGBA frame slots built from four complete-scene pose masters. The sequence holds neutral, slight, medium, and strong road-gaze poses before returning through the same complete-scene masters. Runtime mirroring, crossfade, motion blur, and interpolation are not used.

Owner review rejected v12 because complete-scene generation changed the approved v8 silver vehicle, black/red harness, dog identity, and body proportions. The complete-scene approach removed the local neck seam but did not preserve the visual identity anchor or provide sufficiently continuous pose transitions. Its PNG and provenance files remain preserved as failed evidence.

Current gates: `visual_approved=false`, `runtime_validation=failed_owner_visual_qa`, `runtime_approved=false`, `runtime_use=false`, `prototype_use=false`, and `production_asset=false`. It cannot run in Normal, PrototypePreview, or DeveloperPreview.

Review focus:

- complete car and body geometry consistency between generated pose masters;
- head motion without vertical jumping or a neck seam;
- transparent edge quality on light and dark backgrounds;
- wheel appearance during the approximately two-second gaze sequence.
