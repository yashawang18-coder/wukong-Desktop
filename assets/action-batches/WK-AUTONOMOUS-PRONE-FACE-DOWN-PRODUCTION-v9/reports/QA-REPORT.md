# Wukong prone face-down v9 QA report

Status: `production_candidate_owner_qa_pending`

This candidate uses owner-selected blue-board images 4, 5, and 6 as the
appearance/fur audit reference, and image 6 as the sole production pixel
carrier for F02–F12. No Runway workflow was used.

## Automated results

- F01 and F36 match the approved Down v2 anchor: `c2a6f39a5d3f4db3d14fd80b9f4e8695add95c5e4a5d3e827de83e64b4a5f44d`.
- F11 and F12 are byte-identical.
- F02–F11 use one compact-support deformation field and one pixel carrier.
- Maximum adjacent eye-center horizontal movement: 2.886 px (limit 3 px).
- Maximum adjacent eye/nose ratio change: 4.266% (limit 5%).
- F02–F11 x>=650 rear pixels and y>=790 ground/paw pixels are byte-stable.
- F13–F24 alpha and face/head/front-paw review box are byte-stable.
- F25–F36 are the byte-exact reverse of F12–F01.
- All three GIFs contain 36 physical frames and total 17,240ms.
- Generated settle frames contain zero blue-dominant spill pixels after deterministic defringing.

Automated checks do not grant visual or runtime approval. The full lifecycle,
fur surface, facial character, and WPF transparent rendering remain subject to
owner review and Windows renderer CI.
