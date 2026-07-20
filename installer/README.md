# Quartz Windows installer

Quartz uses Inno Setup to create one branded, per-user Windows installer. The application publish is self-contained, so users do not need to install .NET 8 separately. The Microsoft WebView2 Evergreen bootstrapper is included and runs silently only when WebView2 is missing.

## Build

From the repository root:

```powershell
.\installer\build-installer.ps1
```

The script publishes Quartz for `win-x64`, downloads verified packaging dependencies when necessary, and creates:

```text
release\QuartzSetup.exe
```

Inno Setup is prepared under `.tools\InnoSetup` when no compatible compiler is installed. Pass `-SkipToolBootstrap` to require an existing Inno Setup installation.

## Installer behavior

- Installs per-user to `%LOCALAPPDATA%\Programs\Quartz` without requiring administrator access.
- Creates a Start Menu shortcut.
- Offers an optional Desktop shortcut.
- Registers Quartz in Windows Installed Apps for uninstall.
- Offers to launch Quartz on the completion page.
- Installs WebView2 Evergreen only when its runtime directory is not present.

Before publishing publicly, code-sign `QuartzSetup.exe` and the Quartz application executable with a trusted Windows code-signing certificate.
