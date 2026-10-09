# Panel interaction and portable album follow-up

Local worktree update, 2026-10-09. No asset pixels or approval states change.

## Execution

The gallery submits ControlPanel requests through the existing runtime. Approved
posture transitions are searched as a small explicit graph before a base action.
Every preparation step and the automatic sleep exit retains ControlPanel as its
source. The reducer still tracks execution IDs, Busy and physical end pose, but
does not award needs effects, relationship gains, preferences or recent memories.
Panel executions are also excluded from trigger counters and learned cooldown
history. Card animation inspection remains entirely inside the preview control.

The prior command handler omitted its source argument and accidentally used the
OwnerContextMenu default. It now passes ControlPanel explicitly. Sleep accepts the
panel route only for its already-approved compatible lifecycles. Unknown,
unapproved or missing posture bridges still defer with a visible explanation.
There is no cross-camera hard cut or synthetic transition.

Walking keeps the same PNG timeline and window-motion renderer. A blocked direction
is rejected before playback. Work-area availability updates after window movement
and resizing as well as autonomous ticks. Stop, failure and a replacement request
discard pending panel preparation; stale callbacks cannot resume it.

## Owner experience

- Remove the redundant header Developer button and inactive Settings button. The
  functional developer navigation and authentication remain intact.
- Move relationship metrics to the Owner page. Temperament and daily settings stay
  in Profile. Do not create a second state projection.
- Remove the expired-only gallery filter and exclude deprecated assets from lists;
  historical source files remain untouched.
- Remove the obsolete food/water approval notice. Execution feedback is visible
  above the gallery rather than only underneath its preview.
- Pet-setting debug starts empty when opening the panel. Only model-debug-pet is
  cleared; owner chat, other debug sessions and confirmed memories are preserved.
- Conversation memory is functional: explicit candidate creation, confirmation,
  rejection and deletion operate on the persistent store. The page reports the
  long-term-memory switch plus confirmed/pending counts. Confirmed enabled content
  contributes to dialogue context and bounded decision weights, not direct actions.

## Chat and initiative

Chat placement uses the visible alpha bounds after the complete WPF transform,
including horizontal mirroring. It uses the pet's current monitor, not always the
primary work area. On opening near an edge, the pet moves just enough to make room
for a centered input row underneath it. Source PNGs and scale are unchanged.

Initiative speech already has state-based topic scoring, quiet hours, cooldown,
eight-hour budget, unanswered backoff and relationship/owner feedback. This update
also suppresses speech while dragging, an existing bubble or input is visible,
or an isolated preview is active. Templates avoid repeating the previous sentence.
Spoken text enters daily conversation context so the owner's next reply has context;
model-debug history is not used. No network call is required for initiative speech.

## Portable albums and privacy

The local candidate contains a byte-preserved copy of the owner's images_xhs under
WukongDefaults/albums. On first launch it seeds WukongData/albums once, using the
existing relative `albums` binding. Deleted recipient photos are not restored on
restart. Existing hidden-album markers are preserved. Original files are not moved
or edited. Neither photographs, private descriptions, chat nor credentials are
added to tracked repository defaults or sent to GitHub. Distribute the entire
candidate folder, not just its EXE.

## Validation

See `.publish-check/panel-interaction-v2-verification/` for full test, renderer,
publish and copy-integrity evidence. Final counts and delivery identity are recorded
in CURRENT_STATE.md after execution. Automated launch/render geometry is not owner
visual approval; inspect posture joins, drag/scale/chat behavior at your desktop DPI.
