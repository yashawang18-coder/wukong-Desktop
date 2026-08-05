# Wukong behavior and asset contract foundation

This directory is the A/B/C foundation that precedes completion of P0 artwork.
It deliberately does not promote, replace, or register any existing candidate.

## Layers

- `poses.json`: canonical pose vocabulary used by transition planning.
- `behaviors/`: stable behavior contracts. Behavior IDs never contain asset versions.
- `asset-sidecars/`: non-destructive mappings for legacy/candidate asset packages.
- `runtime/asset-registry.json`: the only runtime binding source. It currently has no bindings.
- `schemas/`: machine-readable contract shapes.

## Lifecycle strategies

Every behavior explicitly declares `intro`, `loop`, `exit`, and
`interrupt_exit`. A segment may use one of these strategies:

- `asset`: play a named approved clip.
- `pose_graph`: plan a route through canonical poses.
- `hold`: remain in the declared stable pose.
- `not_applicable`: the segment is semantically unnecessary.
- `missing`: required P0 material has not been supplied yet.

`missing` is permitted in design contracts but makes a behavior unavailable for
runtime registration. This keeps the P0 gaps visible without inventing assets.

## Commands

```bash
python tools/validate_contracts.py
python tools/generate_pose_graph.py
python -m unittest discover -s tests -p 'test_contract_foundation.py'
```

The validator rejects runtime bindings to anything other than an explicitly
owner-approved, renderer-verified `runtime-approved` asset.

Sidecars also declare manifest availability. `repository` means the manifest is
present on the target branch (with a local provenance path allowed during
migration). `local_unpublished` records evidence that is intentionally absent
from GitHub; it can never claim runtime approval or enter the runtime registry.
