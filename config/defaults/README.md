# Wukong portable defaults

These files are copied to `WukongData` beside the executable only when the
corresponding user file does not already exist.

- `profile/` contains the initial pet, owner, and prompt settings.
- `agent/` contains non-secret model defaults, memory switches, and autonomous
  behavior preferences.
- The initial daily behavior profile favors walking, prone rest, and sleep while
  reducing long standing-idle periods. These are weights, not commands: posture,
  asset approval, cooldowns, and runtime safety remain authoritative.
- API keys are never stored here. They remain in Windows Credential Manager.
- Conversation history and albums are user data and are intentionally absent
  from this source-controlled defaults directory.

Deleting `WukongData/agent/conversation-history.json` clears the portable chat
history. Albums intended to travel with a portable package belong in
`WukongData/albums/` in that package, not in this repository.
