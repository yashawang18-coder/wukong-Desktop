# Alignment provenance

v10 supplied the coherent native-direction head/neck pose artwork. Approved car ride v8 supplied the authoritative body, car, wheel phase, and lower-frame pixels.

Only a deterministic vertical displacement was applied to v10 rows y=210..483. Each pose uses one fixed shift per native direction. The full shift ends at y=420 and eases smoothly to zero at the fixed neck root y=484, preventing a hard seam. Rows y>=484 remain unchanged from v10, and rows y>=650 are verified pixel-identical to the matching approved v8 wheel slot.

No generative redraw, mirror, body scaling, car scaling, color mapping, crop, or frame interpolation was performed for v11.
