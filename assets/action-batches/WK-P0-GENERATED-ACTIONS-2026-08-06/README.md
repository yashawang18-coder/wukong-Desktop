# WK P0 Generated Actions Import

This batch imports approved key-pose records and runtime-candidate transition frames generated on 2026-08-06.

The existing 17 owner-approved keyframes in `WK-BASIC-ACTIONS-BATCH-v2` remain the authority for the base action anchors. They are not overwritten and are not re-submitted in this batch.

Approval boundary:

- `approved-keyframes` means visual key-pose approval only.
- `approved-keyframes` does not mean `runtime-approved`.
- New sit/stand and walk transition animation frames are still `runtime-candidate`.
- `runtime_validation=pending`.
- `runtime_approved=false`.
- `runtime_use=false`.
- Windows real desktop renderer validation is still required before runtime registration or application use.

Import exclusions:

- Source ZIP files.
- `.tmp` files, `__pycache__`, and other caches.
- Contact sheets or contact review images.
- GIF and JPG previews.
- Standing candidate images from the unsuitable standing candidate branch.
- Walk-stop rejected `stop-i1` and `attempt2` evidence.
- Raw videos, video-v2 derived files, video-v2 manifests, and video hash records.
- Private source identifiers.
