# Quartz Browser 2.0

Quartz is a Windows desktop browser built with C#, .NET 8, WPF, CefSharp, and Chromium Embedded Framework (CEF). Version 2.0 replaces Microsoft WebView2 with a packaged Chromium engine and adds experimental support for local unpacked Chromium extensions.

## Features

- Independent multi-tab browsing with favicons and per-tab navigation history
- Address bar URL normalization and DuckDuckGo, Google, or Bing search
- Back, forward, reload, home, page-title, loading, and security state updates
- Local bookmarks, visited-page history, completed-download history, and settings
- CEF download handling with live progress, cancellation, open-file, and open-folder actions
- Quartz new-tab page, theme presets, accent presets, sidebar, and responsive panels
- Custom permission prompts and certificate-error blocking
- Isolated in-memory private browser profiles; private visits and downloads are not added to normal history
- Experimental unpacked Chromium extension manager

Quartz does not include an Agent, AI assistant, Copilot, or chat feature.

## Why CefSharp

Quartz 2.0 uses `CefSharp.Wpf.NETCore` 150.0.110.

| Option | Result |
|---|---|
| CefSharp WPF | Selected. It has a native WPF control, current Chromium/CEF releases, established C# handlers for downloads/permissions/popups, and NuGet packaging for x64 Windows. |
| CefNet WPF | Not selected. Its latest public WPF package is from 2022 and tracks a much older Chromium branch. |
| Custom CEF or Chromium fork | Maximum control, but far more build, security-update, and packaging work than this project can reasonably maintain. |

The official [CefSharp repository](https://github.com/cefsharp/CefSharp) documents its WPF control and current release model. The selected [CefSharp.Wpf.NETCore package](https://www.nuget.org/packages/CefSharp.Wpf.NETCore/150.0.110) supports .NET 8 through framework compatibility.

## Requirements

- 64-bit Windows 10 or Windows 11
- For source builds: .NET 8 SDK
- Visual C++ 2015-2022 x64 runtime (the installer checks and installs Microsoft's signed redistributable when missing)

WebView2 is not required. The Release publish and installer include CEF, Chromium resources, locales, and the .NET runtime.

## Build and run

From PowerShell in the repository root:

```powershell
dotnet restore .\Quartz.sln
dotnet build .\Quartz.sln -c Debug
dotnet run --project .\src\Quartz\Quartz.csproj
```

Release build:

```powershell
dotnet build .\Quartz.sln -c Release
dotnet run --project .\src\Quartz\Quartz.csproj -c Release
```

If `dotnet` is not on `PATH`, use the full path to a .NET 8 SDK executable.

## Unpacked extensions

1. Turn on the Quartz sidebar in Settings if it is hidden.
2. Select **Extensions** (hexagon icon).
3. Select **Load unpacked**.
4. Choose a local folder containing a valid Manifest V3 `manifest.json`.
5. Restart Quartz. Enabled extension folders are passed to Chromium with `--load-extension` at process startup.

Extension records are stored in `%LOCALAPPDATA%\Quartz\extensions.json`. Disabling or removing an extension is saved immediately and takes effect after Quartz restarts. Removing an extension record does not delete its source folder.

### Extension limitations

- Chrome Web Store installation is not implemented.
- CEF 128 and later removed the older dynamic request-context extension API, so Quartz applies extension changes at startup.
- CEF officially supports extensions with Chrome-style windows that expose Chrome UI. Quartz uses CefSharp's WPF off-screen/custom-window control so extensions that do not depend on Chrome toolbar buttons or Chrome-owned UI may work, while others may fail.
- Some Chrome extension APIs, service integration, signing, update, toolbar popup, and Web Store behavior are unavailable or unverified.
- Compatibility must be tested extension by extension. Quartz does not claim full Google Chrome compatibility.

These constraints follow CEF's own guidance: [`--load-extension` can run unpacked extensions, but off-screen/Alloy-style windows only support non-Chrome-UI-dependent extensions case by case](https://www.magpcss.org/ceforum/viewtopic.php?p=56233).

## Browser data

Normal profile data:

- CEF profile: `%LOCALAPPDATA%\Quartz\Chromium`
- Bookmarks: `%LOCALAPPDATA%\Quartz\bookmarks.json`
- History: `%LOCALAPPDATA%\Quartz\history.json`
- Downloads history: `%LOCALAPPDATA%\Quartz\downloads.json`
- Settings: `%LOCALAPPDATA%\Quartz\settings.json`
- Extensions: `%LOCALAPPDATA%\Quartz\extensions.json`

Downloads save to the user's `Downloads` folder. Private windows use an isolated in-memory CEF request context plus a temporary Quartz record folder. Private visits and completed-download records are discarded when that window closes; downloaded files remain on disk. Bookmarks created from a private window intentionally use the normal bookmark store.

The security indicator reflects the active page scheme. CEF performs certificate validation; Quartz rejects certificate errors without an unsafe bypass. Site-data controls remove CEF cookies/cache and page-accessible storage on a best-effort basis. CEF or Chromium may retain storage not exposed through these APIs until the profile is removed.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+T` | New tab |
| `Ctrl+W` | Close active tab |
| `Ctrl+L` | Focus address bar |
| `Ctrl+R` | Reload |
| `Ctrl+D` | Bookmark/unbookmark |
| `Ctrl+H` | History |
| `Ctrl+J` | Downloads |
| `Ctrl+,` | Settings |
| `Ctrl+Shift+N` | New private window |
| `Alt+Left` / `Alt+Right` | Back / forward |
| `Escape` | Close the open side panel |

## Verification checklist

1. Build Debug and Release with zero errors.
2. Launch Quartz and confirm the local Quartz new-tab page appears.
3. Load `https://www.google.com`, `https://www.youtube.com`, and `https://github.com`.
4. Open multiple tabs and verify address, title, favicon, loading, back/forward, reload, and home state stays tab-specific.
5. Bookmark a page, visit pages, restart, and verify normal bookmark/history persistence.
6. Start a download and verify progress, cancellation, completion, file/folder actions, and completed-history persistence.
7. Open a private window and confirm its visits/download records do not enter normal history.
8. Test each theme and the Settings, History, Downloads, Site info, Performance, and Extensions panels at 800x600 and larger sizes.
9. Load a simple unpacked Manifest V3 content-script extension, restart Quartz, and verify its page behavior. Do not use a toolbar-dependent extension as the baseline test.

## Installer and release

Build the self-contained `win-x64` installer:

```powershell
.\installer\build-installer.ps1
```

The script:

1. Publishes Quartz with .NET and all CEF runtime files.
2. Downloads and signature-checks Microsoft's Visual C++ 2015-2022 x64 redistributable when needed for packaging.
3. Uses Inno Setup to create `release\Quartz-2.0.0-Setup.exe`.

The installed application is per-user under `%LOCALAPPDATA%\Programs\Quartz`. It creates a Start Menu entry, optionally creates a Desktop shortcut, registers uninstall information, and includes CEF binaries/resources/locales. Because Chromium is bundled, Quartz 2.0 is hundreds of megabytes unpacked and the installer is much larger than Quartz 1.x.

Before public distribution, sign both `Quartz.exe` and `QuartzSetup.exe` with a trusted Windows code-signing certificate.

## Project structure

```text
src/Quartz/
  App.xaml.cs                 CEF process initialization and profile setup
  BrowserTab.cs               Per-tab CefSharp browser state
  MainWindow.xaml             Quartz browser chrome and panels
  MainWindow.xaml.cs          Navigation, tabs, commands, and panel behavior
  Models/
    BrowserExtension.cs       Persisted unpacked extension model
    DownloadItem.cs           Engine-neutral download view model
  Services/
    CefDownloadHandler.cs     CEF download callbacks
    CefLifeSpanHandler.cs     New-window-to-tab routing
    CefPermissionHandler.cs   Custom permission prompts
    CefRequestHandler.cs      Certificate blocking
    CefDisplayHandler.cs      Favicon URL callbacks
    ExtensionService.cs       Unpacked extension validation/persistence
    DownloadService.cs        Download progress and completed history
installer/                    Inno Setup definition and build script
website/                      Static public download homepage
release/                      Generated installer metadata
```

## Major 2.0 limitations

- The CEF/Chromium application is substantially larger than WebView2 because Quartz ships its own browser engine.
- Direct Chrome Web Store installation is not supported.
- Extension APIs are incomplete in Quartz's custom WPF window; toolbar-dependent extensions are especially limited.
- Quartz must regularly update CefSharp/CEF to receive Chromium security fixes.
- Resource controls remain informational; Quartz does not claim to enforce CPU, memory, or network limits.

See [update.md](update.md) for the release history.
