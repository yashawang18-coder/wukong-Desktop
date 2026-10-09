# Windows installer

`tools/build-windows-installer.ps1` creates two local delivery artifacts:

- a complete self-contained `win-x64` portable ZIP;
- a per-user Inno Setup installer EXE.

The installer writes to `%LOCALAPPDATA%\Programs\Wukong Desktop`, requires no
administrator privilege, and leaves runtime-created `WukongData` in place during
uninstall. First launch enables the app-owned current-user startup entry; the
owner can disable it from the panel.

```powershell
.\tools\build-windows-installer.ps1 -Version 0.1.0-preview.20261009
```

Inno Setup 6 must be installed. Release output is written under
`.publish-check/`, which is intentionally excluded from Git. The build rejects
local review markers, `.asset-staging`, tests, references and existing user data.
The generated installer is currently unsigned, so Windows may show a SmartScreen
warning until a code-signing certificate is added.
