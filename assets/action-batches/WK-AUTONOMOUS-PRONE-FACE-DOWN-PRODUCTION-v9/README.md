# WK-AUTONOMOUS-PRONE-FACE-DOWN-PRODUCTION-v9

Owner-QA candidate for `wk.daily.prone_face_down`.

## Rebuild

From this package directory:

```bash
python tools/build_prone_face_down_v9_local_warp.py \
  source/owner-selected-blue-456/image-06-normalized-carrier.png \
  frames/settle-to-face-down \
  --production-sequence \
  --anchor source/approved-down-v2-terminal-anchor.png
python tools/build_prone_face_down_v9.py --assemble-package .
python tools/finalize_prone_face_down_v9_candidate.py .
```

The candidate is not visually approved or runtime approved. Do not change any
runtime gate until owner full-lifecycle review and Windows renderer CI pass.
