---
name: desktop-ux
description: Improve Wukong's WPF owner or developer panels while preserving owner-facing language, authentication boundaries, shared runtime requests and interaction-state isolation. Use for control-panel or chat UI work.
---

# Desktop UX

Use this skill for WPF control-panel, asset-gallery, profile, album, model,
developer diagnostics or compact chat interaction changes.

## Read First

- `docs/ux/README.md` and the relevant UX HTML reference
- `docs/readme/03-desktop-ui.md`
- The affected XAML/code-behind and focused Desktop tests
- `CURRENT_STATE.md` for accepted visual behavior and owner wording

## Owner And Developer Separation

- Owner pages use concise Chinese descriptions of Wukong's current state and
  actions. Hide asset IDs, versions, frame counts, score internals and raw
  runtime fields behind Developer mode or explicit details.
- Developer pages expose real diagnostics but never bypass production gates,
  developer authentication or preview isolation.
- `查看动画` and `让悟空执行` are different. The latter still submits a real
  semantic request; card inspection must not teach relationship or preferences.

## Workflow

1. Map the requested user task to existing page/tab ownership before adding a
   control. Reuse shared tab, row, toggle and card styles.
2. Keep normal user workflows dense, scannable and keyboard-accessible. Use
   familiar controls: tabs for views, sliders for numeric temperament, toggles
   for binary state and icon buttons for compact commands.
3. Make pending, enabled and expired asset state visible in owner language.
   Expired cards remain previewable only through the expired filter and are not
   actionable.
4. Preserve WPF sizing, DPI and anchor behavior. Do not turn a layout problem
   into image resampling or runtime asset scaling.
5. Verify XAML construction, binding updates, user input focus and the real
   execution route. Respect developer login; tests must not hardcode or bypass
   credentials to capture screenshots.

## Finish

Capture only authorized UI evidence, run focused Desktop tests and state any
Windows visual inspection that cannot be automated. Do not mix a cosmetic panel
change with unrelated behavior-policy or asset approval changes.
