# P0 contract gap report

Generated from contract files; it does not approve any asset.

- `wk.core.prone_idle`: `blocked_pending_renderer_qa`; missing lifecycle segments: none.
- `wk.core.prone_to_stand`: `blocked_pending_hd_runtime_asset`; missing lifecycle segments: interrupt_exit.
- `wk.core.stand_to_prone`: `blocked_pending_hd_runtime_asset`; missing lifecycle segments: interrupt_exit.
- `wk.core.turn_right_front_to_left_front`: `blocked_direction_evidence_conflict`; missing lifecycle segments: interrupt_exit.
- `wk.core.walk_left`: `blocked_missing_intro_exit_and_hd_loop`; missing lifecycle segments: intro, exit, interrupt_exit.

## Catalog-level P0 gaps

- Stable sitting pose and sit/stand transitions are not yet defined.
- Standing idle has no visual candidate sidecar.
- Walk start, walk stop, and safe interrupted stop are absent.
- Only one walk direction is represented.
- The available turn video and approved turn keyframes describe opposite directions.
- Touch, drag/drop, and forced-stop actions are not yet contract-complete.
