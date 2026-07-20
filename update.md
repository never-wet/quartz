# Quartz Update Log

This file tracks major Quartz browser versions, completed work, and known issues.

## Current Version

Current stable version: **2.0**

Latest completed update: **2.0**

## Version 2.0

Quartz 2.0 is the browser-engine migration release.

Completed work:

- Replaced Microsoft WebView2 with CefSharp WPF and Chromium Embedded Framework 150.
- Removed the WebView2 NuGet dependency and all WebView2-specific tab, navigation, profile, download, permission, certificate, favicon, and web-message code.
- Preserved tabs, address/search handling, bookmarks, history, settings, themes, the Quartz new-tab page, private windows, and site-security UI.
- Reimplemented downloads through CEF callbacks with Quartz progress/history/cancel/open actions.
- Reimplemented page popups as Quartz tabs, permission prompts through CEF handlers, certificate blocking, and favicon URL handling.
- Added persistent normal CEF profile data and isolated in-memory private request contexts.
- Added an Extensions panel for validating, enabling, disabling, persisting, and removing local unpacked Manifest V3 extension folders.
- Enabled experimental startup extension loading through Chromium's `--load-extension` switch.
- Updated the self-contained x64 publish and Inno Setup installer to include CEF binaries, resources, and locales instead of installing WebView2.
- Updated version metadata to 2.0.0.

Extension limitations:

- Changes require a Quartz restart because current CEF removed the older dynamic extension API.
- Direct Chrome Web Store installation is not supported.
- Quartz uses a custom WPF/off-screen CEF window. Extensions that depend on Chrome's toolbar or other Chrome-owned UI are not supported reliably; non-toolbar extensions must be tested case by case.
- Some Chrome extension APIs and Google services are unavailable in CEF.

Packaging note: the self-contained Quartz 2.0 publish is roughly 552 MB before installer compression because Chromium/CEF and .NET are bundled.

## Version 1.0

Quartz 1.0 is the first full browser-basics milestone.

Completed features:

- Single-window WebView2 browser shell
- Address bar with URL and search handling
- Back, forward, reload, and home buttons
- Page loading status
- Page title synchronization
- Multi-tab browsing
- New tab and close tab controls
- Bookmarks and bookmarks bar
- Local bookmark storage
- Browser history
- History panel/view
- Local history storage
- Download support
- Custom downloads UI direction
- Settings panel
- Homepage setting
- Search engine setting
- Clear browsing data controls
- Private browsing mode
- Basic security and privacy UI
- Site security indicator
- Site info panel
- Permission prompts
- Theme presets
- Accent color setting
- Sidebar shell
- Custom Quartz new tab page
- Resource controls placeholder

## Version 1.1

Quartz 1.1 is the first cleanup and polish update after the 1.0 browser-basics milestone.

Goals for this update:

- Add a real Quartz app/window/taskbar icon.
- Add website favicons to tabs.
- Improve tab styling and active/inactive tab states.
- Make the top browser chrome more compact.
- Improve downloads so they feel integrated into Quartz.
- Remove or rename fake resource limiter controls.
- Fix theme contrast problems where text becomes invisible.
- Remove duplicate or stray downloads code.
- Fix build/runtime errors from recent changes.

Expected 1.1 acceptance checklist:

- Quartz has an app icon.
- Tabs show favicons.
- Tabs look more polished and compact.
- The top toolbar/tab/bookmarks area is smaller.
- Downloads open in a custom integrated Quartz panel.
- Downloads can be opened, canceled, and cleared.
- Fake CPU/memory/network limiter controls are not presented as working features.
- Text is readable in Light, Dark, Midnight, and Neon themes.
- Duplicate `DownloadsWindow.xaml.cs` outside `src/Quartz` is removed or excluded.
- The project builds successfully.

Release and distribution work completed for 1.1:

- Added a responsive dark Quartz product homepage with an above-the-fold Windows download action.
- Added a custom browser-interface preview, feature overview, system requirements, release notes, and installer metadata.
- Added self-contained `win-x64` publishing so end users do not need to install .NET 8 separately.
- Added a branded Inno Setup installer with Start Menu integration, optional Desktop shortcut, launch option, and Windows uninstall support.
- Added conditional Microsoft WebView2 Evergreen setup for systems where the runtime is missing.
- Added a reproducible packaging script that creates `release/QuartzSetup.exe`.
- Documented local website preview, hosted download-link replacement, installer testing, and the need for production code signing.

## Known Issues To Watch

- Resource controls should not claim to limit CPU, memory, or network unless real limiting is implemented.
- CEF may have limitations around true resource limiting, full Chrome extension support, proprietary media codecs, and complete site-data cleanup.
- Theme changes need careful contrast testing across every panel and control.
- Downloads should use Quartz UI only, not the default Edge-looking downloads UI.
- Tabs need favicon fallback behavior for sites without a standard favicon.
## Tab management update

- Replaced the expanding WPF tab strip with a fixed-height, four-tab row layout.
- Added previous/next row controls; additional tab rows remain internal until selected, and the active row is revealed automatically.
- Added the Quartz **All tabs** manager with styled fallback previews, close controls, active-tab highlighting, and confirmed clear-all behavior.
- Added `Ctrl+Shift+A` to open or close the tab manager; `Escape` closes it.
