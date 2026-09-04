# Whole-frame normalization

The v12 masters were normalized as indivisible complete scenes.

- Canvas: 1024 x 1024 RGBA
- Target visible width: 930 px
- Shared wheel baseline: y=900
- Horizontal placement: center of canvas
- Allowed transform: uniform scale plus whole-image translation
- Forbidden transform: head-only, neck-only, body-only, car-only, or wheel-only edit
- Alpha cleanup: connected magenta key removal followed by a one-pixel full-subject matte contraction before final downsampling

This normalization reduces generated canvas and baseline variance. It does not claim that AI-generated geometry is production approved. The complete animated sequence still requires owner review in the Windows transparent WPF renderer.

