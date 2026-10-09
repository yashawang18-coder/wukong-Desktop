# Windows installer

`tools/build-windows-installer.ps1` creates two local delivery artifacts:

- `deskpet-portable.zip`, a complete self-contained `win-x64` portable ZIP;
- `deskpet.exe`, a per-user Inno Setup installer.

The delivery package keeps the default configuration, the explicitly bundled
album seed and every canonical batch required by current behavior definitions. Historical
and superseded batches, source masters, review GIFs, contact sheets and mutable
`WukongData` are excluded from delivery without deleting their repository copies.

The installer writes to `%LOCALAPPDATA%\Programs\Wukong Desktop`, requires no
administrator privilege, and leaves runtime-created `WukongData` in place during
uninstall. First launch enables the app-owned current-user startup entry; the
owner can disable it from the panel.

```powershell
.\tools\build-windows-installer.ps1 -Version 0.1.0-preview.20261009
```

Inno Setup 7 is recommended for the repository's long asset paths; version 6 is
also supported when source paths remain within its limits. Release output is written under
`.publish-check/`, which is intentionally excluded from Git. The build rejects
local review markers, `.asset-staging`, tests, references and existing user data.
The generated installer is currently unsigned, so Windows may show a SmartScreen
warning until a code-signing certificate is added.
