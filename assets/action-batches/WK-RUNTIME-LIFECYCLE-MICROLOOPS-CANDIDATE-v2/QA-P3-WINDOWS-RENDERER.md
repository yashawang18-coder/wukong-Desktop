# P3 Windows Renderer QA

Date: 2026-08-15
Result: passed
Reviewer: owner manual transparent-window acceptance

Scope accepted:

- Complete lifecycle intro / loop / exit / interrupt_exit.
- stand-idle, sit-idle, and prone-idle six-frame microloops.
- 128px, 192px, and 256px candidate preview sizing.

Owner acceptance notes:

- Four new sequences are visually good overall.
- Complete lifecycle playback, exit, and interruption are normal.
- stand-idle, sit-idle, and prone-idle loops are normal.
- No visible foot jitter, transparent edge issue, or y=930/y=932 artifact was observed.

Strict alpha audit retained:

A strict non-zero-alpha bbox audit reported 17 frame references extending to y=932. This was not hidden by changing global validation thresholds. The approved PNG files were not cropped, recolored, resized, or baseline-shifted after manual acceptance.

Runtime activation range:

- `wk.candidate.lifecycle.lively_daily_p2`: low-frequency self-directed daily behavior.
- `wk.candidate.lifecycle.stand_idle_microloop`: stable stand only.
- `wk.candidate.lifecycle.sit_idle_microloop`: stable sit only.
- `wk.candidate.lifecycle.prone_idle_microloop`: stable prone only.

Non-scope:

- Not right-click menu.
- Not command/training action.
- Not magic, coin, Cybertruck, album, tab style, or old asset recolor work.
