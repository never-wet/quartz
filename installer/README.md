# Quartz Windows installer

Quartz uses Inno Setup to create one branded, per-user Windows installer. The application publish is self-contained: it includes .NET 8 and the CefSharp/Chromium runtime files, so users do not need a separate WebView2 or .NET installation.

## Build

From the repository root:

```powershell
.\installer\build-installer.ps1
```

The script publishes Quartz for `win-x64`, downloads verified packaging dependencies when necessary, and creates:

```text
release\Quartz-2.0.0-Setup.exe
```

Inno Setup is prepared under `.tools\InnoSetup` when no compatible compiler is installed. Pass `-SkipToolBootstrap` to require an existing Inno Setup installation.

## Installer behavior

- Installs per-user to `%LOCALAPPDATA%\Programs\Quartz` without requiring administrator access.
- Creates a Start Menu shortcut.
- Offers an optional Desktop shortcut.
- Registers Quartz in Windows Installed Apps for uninstall.
- Offers to launch Quartz on the completion page.
- Installs all CEF binaries, resources, and locales from the self-contained publish.
- Installs Microsoft's signed Visual C++ 2015-2022 x64 Redistributable only when it is missing; Windows may request elevation for that prerequisite.

The Chromium/CEF payload makes Quartz 2.0 substantially larger than the previous WebView2-based release.

Before publishing publicly, code-sign `QuartzSetup.exe` and the Quartz application executable with a trusted Windows code-signing certificate.
