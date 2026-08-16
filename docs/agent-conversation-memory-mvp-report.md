# Wukong Agent Conversation and Memory MVP Report

## Repository State

- Starting branch: `agent/control-panel-ux-phase15-v2`
- Working branch: `agent/conversation-memory-mvp`
- Base commit: `c29dfbfd7814d5f55dcf1297aad126686c9065ef`
- Starting worktree: clean
- Safety checkpoint: not required because the worktree was clean
- Final commit: see the final terminal summary; a Git commit cannot contain its own SHA
- Push, PR, main merge, tag, and formal release: not performed

The work stayed outside `main`. No action image, action manifest, runtime asset registry,
playback lifecycle, command-action V3 implementation, or formal personality baseline was changed.

## Before This Change

The model page was a visual configuration form. It wrote a non-secret text marker and its chat
button called the desktop local FakeModel path. There was no network provider adapter, shared
conversation service, context assembler, short-term history, memory candidate workflow, or album
retrieval for model requests. The saved pet prompt and profile forms did not feed a real model
request. Developer mode was a visibility toggle without password or service-layer authorization.
The desktop pet had no collapsible daily chat surface.

## Architecture

The shared call chain is:

```text
MainWindow / ControlPanelWindow
  -> DesktopAgentRuntime (local composition root)
  -> ContextualConversationService
  -> LocalPetContextProvider
       -> LocalAgentProfileStore
       -> IRuntimeContextStateProvider
       -> AlbumMarkdownMemoryRetriever
       -> FileConversationMemoryStore
  -> AgentContextAssembler
  -> ConfiguredChatModelRuntime
  -> selected IChatModelProvider
```

`DesktopAgentRuntime` owns one conversation service, provider runtime, profile store, history,
memory candidate store, developer session, diagnostic service, and mock state provider. The panel
and desktop chat receive the same instance from `MainWindow`, so they share provider selection,
history, retrieval, and context. The existing desktop behavior runtime remains a clearly labelled
local/fallback runtime and is not presented as the production behavior pipeline.

The conversation layer depends on `IPetContextProvider` and `IRuntimeContextStateProvider`, not on
WPF windows, image players, or behavior internals. The current state implementation is
`MockRuntimeContextStateProvider`. A future behavior kernel can implement
`IRuntimeContextStateProvider` and replace it only in the composition root.

## Context Assembly

The assembler applies this order:

1. Fixed safety and truthfulness boundary.
2. Pet identity and profile facts.
3. User-saved pet setting.
4. Owner profile.
5. Read-only personality, relationship, and runtime state.
6. Up to five relevant album memories.
7. Bounded short-term history.
8. Current user message.

The default character budgets are 12,000 total, 2,500 for profiles/settings, 3,500 for album
memory, and 5,000 for at most 12 history messages. Truncation and degradation reasons are visible
only through authenticated developer diagnostics. Album text is placed in a separately labelled,
untrusted reference-data block; Markdown instructions cannot replace the system boundary.

## Profiles, Albums, and Memory

- Pet profile: `%LOCALAPPDATA%\Wukong\profile\pet-profile.json`
- Owner profile: `%LOCALAPPDATA%\Wukong\profile\owner-profile.json`
- Pet setting: `%LOCALAPPDATA%\Wukong\profile\pet-prompt.txt`
- Provider metadata, history, and candidates: `%LOCALAPPDATA%\Wukong\agent`
- Album root precedence: `WUKONG_ALBUM_ROOT`, saved user preference, development fallback, then the
  user's Pictures directory

Album retrieval recursively scans a bounded number of small Markdown files and scores title, date,
body terms, media names, and simple word/CJK-bigram matches. It returns zero results when there is
no relevant match and safely skips missing, inaccessible, oversized, or damaged Markdown. It is
read-only and never edits images or Markdown.

Short-term history keeps at most 20 persisted messages for the shared daily session. Failed model
responses do not create assistant history. Users can clear the current conversation. A completed
turn can become a pending memory candidate; only manual confirmation makes it eligible for later
context. Candidates can be confirmed, rejected, or deleted. They never directly modify profiles,
the personality baseline, or albums.

## Model Providers

| Provider | Protocol | Fields | MVP status |
| --- | --- | --- | --- |
| OpenAI | `POST /v1/chat/completions` | API Key, base URL, model, timeout, temperature | Implemented and Fake HTTP tested |
| OpenAI-compatible | Chat Completions-compatible endpoint | API Key, base URL, model, timeout, temperature | Implemented and Fake HTTP tested |
| Anthropic | Native Messages API | API Key, base URL, model, timeout, temperature | Implemented and Fake HTTP tested |
| Gemini | Native `generateContent` | API Key, base URL, model, timeout, temperature | Implemented and Fake HTTP tested |
| Ollama | Native `POST /api/chat` | local URL, model, timeout, temperature | Implemented and Fake HTTP tested |
| Azure OpenAI | Reserved through provider abstractions | Not exposed | Not implemented in this MVP |

Users select a provider before editing it. Each provider retains separate metadata and credentials;
model names remain free-form. Ollama disables the API Key field. Connection tests are explicit user
actions. The common error map covers configuration, 401, 403, 404, 429, 5xx, timeout,
cancellation, network failure, and empty response. Failures never fabricate a Wukong reply.

No paid or real external model request was run during automated verification.

## Credentials and Privacy

Provider metadata is stored as JSON without secret values. API keys are stored separately per
provider in Windows Credential Manager and are never read back into the UI. The UI displays only a
configured/not-configured state. Requests and diagnostics do not log headers, full keys, raw HTTP
payloads, full prompts, full local paths, or stack traces to ordinary users.

The credential pasted into the task prompt was not used, saved, logged, tested, or copied into any
repository file. It should be revoked and replaced because it was exposed in conversation text.
Repository scans found no matching value.

Local profile, album, avatar, chat, credential, log, cache, build, and publish data remains outside
Git. Existing demo-only values in the UX HTML are not used by the runtime.

## Developer Boundary

The existing Wukong developer password `0714` is reused. Wrong passwords do not authenticate;
sign-out clears the in-memory session. The developer page and toggle prompt for authentication.
More importantly, `DeveloperDiagnostics.ReadLatest` and `MockRuntimeContextStateProvider.Update`
independently reject unauthenticated calls, so access is not protected only by WPF visibility.

Authenticated diagnostics include provider/model, duration, status, injected field names, a short
pet-setting summary, read-only state, album title/date/score/source filename, history count, and
truncation reasons. They never include a full API key or Authorization header.

## Desktop Chat

The desktop chat is hidden by default. Clicking the lower central transparent sensor area toggles
it; clicking the visible body continues through the existing body interaction path. The overlay
supports Enter to send, Shift+Enter for a newline, Esc and a button to collapse, explicit request
cancellation, a 45-second idle collapse, and shared history. Collapsing does not cancel a request.

`DesktopChatPlacement` prefers below the pet, opens upward when the bottom edge has insufficient
space, and clamps the overlay to the supplied work area. Placement is covered at all four corners.
Multi-monitor per-monitor work-area selection remains a manual acceptance item; the current host
supplies WPF's active system work area.

## UX Review

The model page now follows the current Wukong card, color, spacing, type, and navigation system. It
contains editable provider configuration, real connection testing, memory capability states, the
saved pet-setting editor, and shared conversation. Unimplemented vector retrieval, cloud sync,
automatic summarization, and general settings are visibly marked as unavailable.

Visible Release inspection found and fixed stretched DockPanel children, an oversized Sync button,
a full-row credential badge, an oversized developer lock card, misleading FakeModel status, and
an action/phase footer collision. Default 1240x800 and minimum 920x660 windows were rendered after
the fix. The narrow page scrolls without overlap or horizontal layout breakage.

Local ignored visual evidence:

- `.publish-check/agent-conversation-memory-mvp-acceptance/release-pet-final.png`
- `.publish-check/agent-conversation-memory-mvp-acceptance/release-control-owner-final.png`
- `.publish-check/agent-conversation-memory-mvp-acceptance/release-control-model-final.png`
- `.publish-check/agent-conversation-memory-mvp-acceptance/release-control-model-narrow.png`

The pet image was rendered by the actual published EXE. Panel images were rendered from the same
Release product assembly through the Desktop test host because the remote desktop session did not
expose the transparent layered window's context menu to external UI Automation.

## Files

New product files:

- `src/Wukong.Application/AgentContracts.cs`
- `src/Wukong.Application/AgentConversation.cs`
- `src/Wukong.Infrastructure/ChatModelProviders.cs`
- `src/Wukong.Infrastructure/AgentLocalStores.cs`
- `src/Wukong.Desktop/DesktopAgentRuntime.cs`
- `src/Wukong.Desktop/DesktopChatPlacement.cs`
- `src/Wukong.Desktop/DesktopChatWindow.xaml`
- `src/Wukong.Desktop/DesktopChatWindow.xaml.cs`
- `src/Wukong.Desktop/DeveloperLoginWindow.xaml`
- `src/Wukong.Desktop/DeveloperLoginWindow.xaml.cs`

Modified product files:

- `src/Wukong.Desktop/MainWindow.xaml`
- `src/Wukong.Desktop/MainWindow.xaml.cs`
- `src/Wukong.Desktop/ControlPanelWindow.xaml`
- `src/Wukong.Desktop/ControlPanelWindow.xaml.cs`
- `src/Wukong.Desktop/DesktopPetRuntime.cs` (status label only)

Tests and documentation:

- `tests/Wukong.Application.Tests/Program.cs`
- `tests/Wukong.Infrastructure.Tests/Program.cs`
- `tests/Wukong.Desktop.Tests/Program.cs`
- `docs/agent-conversation-memory-mvp-report.md`

## Verification

Commands and results:

```text
dotnet format Wukong.sln --no-restore --verify-no-changes --verbosity minimal
PASS

dotnet build Wukong.sln --no-restore -v minimal
PASS, 0 warnings, 0 errors

python -B -m unittest discover -s tests
PASS, 25/25

python -B tools\validate_contracts.py
PASS, 0 errors, 9 pre-existing known lifecycle gaps

Domain / Contracts / Application / Infrastructure / Desktop executable tests
PASS, 5 + 5 + 21 + 14 + 19 = 64/64

dotnet build src\Wukong.Desktop\Wukong.Desktop.csproj -c Release ...
PASS, 0 warnings, 0 errors

dotnet publish src\Wukong.Desktop\Wukong.Desktop.csproj -c Release ...
PASS
```

There are 89 automated tests across Python and .NET. This change added 22 .NET checks covering
context assembly, injection boundaries, budgets, history, memory candidates, provider request
formats and errors, credential isolation, album degradation, developer authorization, XAML
construction, desktop chat keyboard/sensor behavior, and edge placement.

The first Release attempt reproduced the machine's silent `0 warnings / 0 errors` exit-1 issue.
Inspection found 60 orphaned MSBuild node-reuse processes. After stopping only those build-server
nodes, Release succeeded with `-m:1 -nodeReuse:false -p:UseSharedCompilation=false`.

Published output:

- Directory: `.publish-check/agent-conversation-memory-mvp`
- EXE: `.publish-check/agent-conversation-memory-mvp/Wukong.Desktop.exe`
- Type: framework-dependent
- Required runtime: `.NET 8`, `Microsoft.WindowsDesktop.App 8.0.0`
- Output inventory: 189 files, including 147 PNG and 31 JSON files

## Manual Acceptance Checklist

- [ ] Ask: “悟空，你叫什么？”
- [ ] Ask: “你知道我是谁吗？” after saving the owner profile.
- [ ] Ask about a recorded date/place that exists in the selected album root.
- [ ] Ask about an experience that does not exist; verify Wukong does not invent it.
- [ ] Change trust in authenticated Mock state and repeat the same question.
- [ ] Change fatigue and stress and repeat the same question.
- [ ] Change and save the pet setting; verify a restrained tone difference.
- [ ] Point the album preference to a missing directory and verify safe empty retrieval.
- [ ] Clear profile fields and verify the response does not invent values.
- [ ] Save an invalid model URL/model and verify a friendly error.
- [ ] Test while offline.
- [ ] Test request timeout.
- [ ] Explicitly cancel a request.
- [ ] Collapse desktop chat during a request and verify it continues.
- [ ] Clear shared short-term history from the panel.
- [ ] Save a completed turn as a pending memory candidate.
- [ ] Confirm, reject, and delete separate candidates.
- [ ] Test personal configurations for OpenAI-compatible, Claude, Gemini, and Ollama.
- [ ] Verify ordinary mode cannot read diagnostics or Mock values.
- [ ] Verify wrong developer password fails and `0714` succeeds.
- [ ] Click the lower transparent pet area to expand/collapse chat.
- [ ] Verify body clicks and drag do not open chat.
- [ ] Place the pet at each monitor edge and corner; verify overlay placement.
- [ ] Verify panel and overlay at 125% and 150% display scaling.

## Remaining Limits

- No real external provider call was made because verification deliberately used no real key.
- Azure OpenAI is an extension point, not a configured provider in this MVP.
- Runtime context is Mock/fallback state until the behavior kernel supplies an
  `IRuntimeContextStateProvider` adapter.
- Long-term memory is an explicit confirmed-candidate list, not a vector store or autonomous
  memory-writing agent.
- Album retrieval is lexical and local; it does not perform OCR or image understanding.
- Per-monitor work-area behavior and 125%/150% DPI require final interactive verification.
- Transparent lower-sensor clicking requires final interactive verification; its geometry,
  body/sensor separation, and overlay placement are automated.
- No real API credential, paid request, push, PR, main merge, tag, or formal release was performed.
