# Asset directory conventions

## Canonical layout

```text
assets/
├── identity/
│   └── <character-profile>/
│       ├── identity-board.png
│       ├── identity-spec.md
│       ├── manifest.json
│       ├── README.md
│       └── anchors/
│           ├── front.png
│           ├── left-front-35.png
│           ├── right-front-35.png
│           ├── left-side.png
│           ├── rear-three-quarter.png
│           └── <expression>.png
├── actions/
│   └── <asset-id>/
│       ├── asset.json
│       ├── README.md
│       ├── candidates/
│       │   └── v<version>/
│       │       └── frame-<nnn>.png
│       ├── approved-keyframes/
│       ├── runtime-frames/
│       ├── previews/
│       └── prompts/
└── atlases/
    └── legacy-reference/
```

Git does not track empty directories. Optional directories are created when they receive their first file.

## Status lifecycle

| Status | Meaning | Runtime use |
|---|---|---|
| `candidate` | Awaiting owner review and/or motion validation | Forbidden |
| `approved-keyframes` | Identity and pose approved as interpolation anchors | Not yet |
| `runtime-candidate` | Full sequence ready for playback QA | Test only |
| `runtime-approved` | Identity, motion, transparency, and real-renderer integration checks passed | Allowed |
| `rejected` / `deprecated` | Retained only for audit history | Forbidden |

Owner preview approval must be represented separately from runtime approval. Until real desktop-renderer validation passes, keep `runtime_approved=false` and `runtime_use=false`.

## Identity hierarchy

1. Approved identity board and closest direction anchor.
2. Private real-photo evidence, never committed to this public repository.
3. Approved keyframe for the current action.
4. Legacy atlases for pose and motion only.
5. Text instructions.

New frames must be local motion deltas from approved anchors. Independent full-frame regeneration is prohibited because it causes identity drift.

## Shared behavior bindings

When a new behavior meaning uses an exact contiguous range from an existing approved motion, store a reference binding instead of copying PNG files. The binding must include the canonical source asset batch, behavior ID, phase, one-based start frame, frame count, and SHA-256 of the concatenated source frame bytes.

- The source motion must be current, runtime-approved, and explicitly allowed for that binding domain.
- The binding owns its own semantic and runtime gates; source approval never promotes the new behavior meaning automatically.
- Missing sources, changed bytes, invalid ranges, expired sources, or disabled sources fail closed.
- Timing is inherited from the exact source phase and range.
- Duplicate PNG storage, implicit filename matching, pixel processing, and fallback to legacy/expired visuals are forbidden.

## Privacy and review rules

- Do not commit real photographs of Wukong.
- Identity packages must declare whether they contain real photos.
- Candidate actions stay outside runtime manifests.
- Approval of an identity board does not approve an action sequence.
- Every action package must record identity, motion, and technical review separately.
- Preserve rejected versions only when audit value outweighs repository size; never load them at runtime.

## Naming

- Character profile: `wukong-current-adult-v1`
- Action ID: `WK-<DOMAIN>-<ACTION>-<DIRECTION>`
- Frame: `frame-001.png`
- Asset branch: `agent/assets-<short-description>`
