# Quartz Update Log

This file tracks major Quartz browser versions, completed work, and known issues.

## Current Version

Current stable version: **1.0**

Latest completed update: **1.1**

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

## Known Issues To Watch

- Resource controls should not claim to limit CPU, memory, or network unless real limiting is implemented.
- WebView2 may have limitations around deep browser features like true resource limiting, full extension support, and complete private-profile cleanup.
- Theme changes need careful contrast testing across every panel and control.
- Downloads should use Quartz UI only, not the default Edge-looking downloads UI.
- Tabs need favicon fallback behavior for sites without a standard favicon.
