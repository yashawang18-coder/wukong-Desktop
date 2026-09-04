# Wukong Command Production Candidates v4

This directory imports the local `wukong-eight-command-production-candidates-v4` source bundle for owner command runtime use.

The frames are real Wukong command production candidates, replacing the earlier rough cartoon command mock frames in the local agent mock path. They are approved only for explicit owner command execution from the context menu and control panel command asset page:

- `motion_design_approved=true`
- `production_asset=true`
- `visual_approved=true`
- `runtime_approved=true`
- `runtime_use=true`
- `prototype_use=false`
- `asset_stage=runtime_approved_owner_command`

Supported command branches:

- Sit: `stable_stand -> stable_sit`
- Down: `stable_sit -> stable_prone`
- PawSit: `stable_sit -> stable_sit`
- PawProne: `stable_prone -> stable_prone`
- Jump: `stable_stand -> stable_stand`
- Spin: `stable_stand -> stable_stand`
- EatSit: `stable_sit -> stable_sit`
- EatProne: `stable_prone -> stable_prone`

Paw and Eat are selected by the current `StablePosture`. These assets must not be triggered by autonomous behavior, dialogue/model routing, startup autoplay, or unrelated interaction paths.

The older `WK-COMMAND-ACTION-CANDIDATES-v3` batch is retained as an expired motion reference in the asset panel. It is not used by the owner command execution path.
