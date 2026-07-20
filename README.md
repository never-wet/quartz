# Quartz

Quartz is a small Windows web browser prototype. It uses C#, .NET 8, WPF, and Microsoft Edge WebView2 (Chromium).

## Version 0.10 features

- One browser window with multiple tabs
- New tab button
- Close tab button
- `Ctrl+T` to open a tab
- `Ctrl+W` to close the active tab
- Address bar with URL handling and selectable DuckDuckGo, Google, or Bing search
- Back, forward, reload, and home controls
- Configurable homepage used by Home and new tabs
- Configurable startup behavior: homepage or blank page
- Page URL and title synchronization
- Navigation-aware back and forward buttons
- Loading and status indicators
- Simple startup and navigation error messages
- Links that request a new window open in a new tab instead
- JSON-backed bookmarks that remain after restarting Quartz
- Bookmark star button and `Ctrl+D` toggle
- Bookmarks bar with open and remove controls
- Duplicate bookmark prevention by exact URL
- Active bookmark state that follows navigation and tab selection
- Persistent JSON browsing history with page title, URL, and visit time
- Newest-first History panel with clickable entries
- History toolbar button and `Ctrl+H` toggle
- Clear-all history action with confirmation
- Successful HTTP/HTTPS visits only, capped at the latest 500 entries
- Consecutive duplicate URL suppression
- WebView2 download detection with live progress and status
- Default saving to the user's Downloads folder with collision-safe names
- Completed-only JSON download history that survives restarts
- Clear completed download history without affecting active, canceled, or failed items
- Removed the experimental Agent UI; Quartz contains only browser features
- Downloads run through WebView2 and save directly to the user's Downloads folder; the Downloads toolbar button and `Ctrl+J` open a separate manager window
- JSON-backed Settings panel with toolbar and `Ctrl+,` access
- Confirmed controls to clear history, downloads history, bookmarks, or all three
- Private browsing windows with isolated temporary WebView2 profiles
- `Ctrl+Shift+N` and the Private toolbar button open a private window
- Private windows do not write to normal history or downloads history
- Address-bar security indicator for HTTPS, HTTP, and local/internal pages
- Site information panel with connection details and per-site data clearing
- Quartz permission prompts for camera, microphone, location, and notifications
- Certificate errors are canceled and shown as clear Quartz security warnings
- Settings privacy/security shortcuts for site data and private windows
- Shared Quartz design system with a neutral canvas, white surfaces, and a violet accent
- Rounded active tabs, quieter inactive tabs, and trimmed long tab titles
- Compact icon toolbar with consistent sizing, hover/pressed states, disabled states, and tooltips
- Polished address bar, bookmarks bar, security state, loading indicator, and status area
- Consistent Quartz cards, headers, spacing, and actions across History, Settings, site information, permissions, and Downloads
- Responsive browser chrome and scrolling side panels for windows from 800x600 upward
- A subtle private-mode badge and private color treatment that remains distinct without obscuring browsing
- `Escape` closes the open History, Settings, or site-information panel
- Four built-in Quartz theme presets: Light, Dark, Midnight, and Neon
- Five accent presets that update progress, active-tab, selection, and primary-action colors
- Immediate theme/accent preview with JSON persistence across restarts
- Optional compact Quartz sidebar with routes to bookmarks, History, Downloads, Settings, and Resource Controls
- A local Quartz new-tab page with branded styling, address/search input, and bookmark-backed quick links
- New-tab styling follows the selected theme and accent without loading an external website
- Resource Controls preview with clearly disabled Memory saver, CPU limiter, and Network limiter placeholders
- No assistant, chat, or automated browsing feature is included

## Prerequisites

1. Windows 10 or Windows 11.
2. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Microsoft Edge WebView2 Runtime. It is included with current Windows and Microsoft Edge installations. If needed, install the Evergreen Runtime from the [official WebView2 download page](https://developer.microsoft.com/microsoft-edge/webview2/).

Visual Studio 2022 with the **.NET desktop development** workload can be used instead of the command line.

## Build and run

From the repository root:

```powershell
dotnet restore Quartz.sln
dotnet build Quartz.sln --configuration Release
dotnet run --project .\src\Quartz\Quartz.csproj
```

In Visual Studio, open `Quartz.sln`, set `Quartz` as the startup project, and press `F5`.

## Using Quartz

- Enter a full URL such as `https://github.com` and press Enter.
- Enter a domain such as `youtube.com` and press Enter; Quartz adds HTTPS.
- Enter words such as `WPF WebView2` and press Enter; Quartz uses the selected search engine.
- Press `Ctrl+L` to focus the address bar or `Ctrl+R` to reload.
- Press `Ctrl+T` to open a new tab.
- Press `Ctrl+W` to close the active tab.
- Press `Ctrl+D` to bookmark or unbookmark the active page.
- Press `Ctrl+H` to open or close the History panel.
- Press `Ctrl+,` to open or close Settings.
- Press `Ctrl+Shift+N` or click **Private** to open a private window.
- Press `Ctrl+J` to open the separate Downloads window.
- Press `Alt+Left` and `Alt+Right` to move through page history.
- Click a bookmark title to open it in the active tab, or its remove button to delete it.
- Click a History entry to open it in the active tab, or use **Clear all** to remove every stored visit.
- Use the separate Downloads window to monitor progress, cancel active transfers, open completed files, open their folder, or clear completed records.
- Use Settings to change the homepage, search engine, startup page, or clear stored browsing data.
- Use Settings > **Appearance** to switch theme and accent presets or show/hide the sidebar.
- Click the sidebar toggle at the start of the toolbar to collapse or restore the sidebar.
- Enter a URL or search in the Quartz new-tab page, or use one of its bookmark/static quick links.
- Open **Resource controls** from the bottom sidebar button to view the explicitly nonfunctional performance preview.

Bookmarks are stored locally at `%LOCALAPPDATA%\Quartz\bookmarks.json`. If that file is missing or contains invalid JSON, Quartz starts with an empty bookmarks bar.

Browsing history is stored at `%LOCALAPPDATA%\Quartz\history.json`. Missing or invalid history files are treated as empty, and Quartz retains at most the newest 500 valid entries.

Downloads are saved to `%USERPROFILE%\Downloads` by default. Completed-download history is stored at `%LOCALAPPDATA%\Quartz\downloads.json` and retains the newest 200 completed items. Canceled, interrupted, and failed transfers are tracked internally but are not saved as completed history.

Browser preferences are stored at `%LOCALAPPDATA%\Quartz\settings.json`. This includes homepage, search engine, startup behavior, theme, accent, and sidebar visibility. Missing, invalid, or corrupted settings files safely fall back to the DuckDuckGo homepage/search engine, Light theme, Quartz violet accent, visible sidebar, and homepage startup behavior.

Quartz's new-tab page is generated locally with `CoreWebView2.NavigateToString`; it does not request a hosted Quartz page. The page sends only the submitted address/search text to the owning tab through WebView2's web-message bridge. Bookmark URLs are HTML-encoded before being displayed as quick links.

Private windows use a temporary WebView2 user-data directory and a temporary history/downloads record store. The temporary profile is deleted when the private window closes; downloaded files themselves remain in the normal Downloads folder. Private bookmarks intentionally use the normal bookmark store. WebView2 profile cleanup is best-effort if the runtime still has a file lock during shutdown.

The security indicator reports the active URL's connection scheme after WebView2 navigation: HTTPS is shown as secure, HTTP as not secure, and non-web pages as local/internal. WebView2 performs the actual certificate validation; Quartz cancels certificate-error events and does not provide an unsafe bypass. The per-site clear button removes cookies through WebView2 and clears storage exposed to the currently loaded origin. Browser-managed or partitioned storage that is not exposed to the page may require the profile-wide **Clear cookies and site data** Settings action. Permission choices apply to the current request and are not permanently saved. WebView2 exposes notification permission requests, but push notifications remain limited by the runtime.

## Verification checklist

Use this Step 10 identity-feature smoke test:

1. Open Settings > **Appearance** and select Light, Dark, Midnight, and Neon. Confirm each updates the window, toolbar, tabs, address bar, panels, progress color, and new-tab page immediately.
2. Choose each accent preset and confirm the active-tab indicator, focused address border, important buttons, progress UI, and selected states use the new accent.
3. Restart Quartz and confirm the selected theme, accent, and sidebar visibility persist.
4. Collapse and restore the sidebar with the toolbar menu button. Use its Bookmarks, History, Downloads, and Settings buttons and confirm they route to the existing Quartz views without closing the browser.
5. Open a new tab. Confirm the local Quartz identity page appears, bookmark/static quick links work, and entering both a domain and a search phrase navigates the active tab.
6. Open **Resource controls** from the sidebar. Confirm Memory saver, CPU limiter, and Network limiter are disabled and the panel explicitly says resource limiting is not implemented.
7. Open a private window and confirm themes, the sidebar, and the new-tab page work while private history/download isolation remains unchanged.
8. Recheck normal tabs, navigation, bookmarks, History, Downloads, Settings, site information, permissions, and all existing keyboard shortcuts.

Use this Step 9 UI-polish smoke test:

1. Run Quartz at 800x600, 1366x768, and 1920x1080. Confirm the toolbar remains aligned, the address bar stays usable, and no panel extends beyond the window.
2. Open several tabs with long titles. Confirm the active tab is obvious, inactive tabs remain readable, and long titles trim with an ellipsis.
3. Hover and press the navigation, bookmark, History, Downloads, Settings, and Private buttons; confirm their states are consistent and tooltips identify each command.
4. Open a new tab with `Ctrl+T` and confirm focus moves to the address bar. Close it with `Ctrl+W` and confirm Quartz selects a nearby tab.
5. Open History, Settings, and the site-information panel. Confirm the shared card styling and scrolling behavior, and use `Escape` to close each panel.
6. Open Downloads and a permission request. Confirm both use the same Quartz typography, accent color, borders, and button language as the main window.
7. Open a private window and confirm the private badge and subtle color treatment are visible without changing the overall layout.
8. Recheck navigation, searches, bookmarks, History, Downloads, Settings, security status, permission prompts, and private browsing.

Use this Step 8 security/privacy smoke test:

1. Open `about:blank`, an HTTPS page, and an HTTP page; confirm the indicator shows **Local**, **Secure**, and **Not secure** respectively.
2. Switch between those tabs and confirm the indicator follows the active tab.
3. Click the indicator and verify the site domain, full URL, connection status, privacy note, and clear-site-data control.
4. Trigger camera, microphone, location, and notification requests from a test site. Confirm Quartz shows its own Allow/Block prompt and closing the prompt blocks the request.
5. Visit a site with an invalid certificate and confirm Quartz cancels it and shows a security warning without a bypass action.
6. Open Settings and test **Clear cookies and site data** and **Open a private window**.
7. Recheck normal/private tabs, navigation, bookmarks, History, Downloads, and searches.

Use this Step 7 private-mode smoke test:

1. Click **New Private Window** and press `Ctrl+Shift+N`; confirm each opens a window whose title and toolbar identify Private Mode.
2. Visit a page in a normal window and another page in a private window. Confirm only the normal visit appears in normal History.
3. Open Downloads from a private window and confirm it is labeled **Private Downloads**.
4. Complete a private download. Confirm the file remains in the Downloads folder but its record does not appear in the normal Downloads window after the private window closes.
5. Create a bookmark privately and confirm it appears normally; Quartz intentionally shares bookmarks with private windows.
6. Close the private window and confirm its temporary `Quartz-Private-*` WebView2 profile is removed without changing normal history, bookmarks, downloads, or settings.
7. Confirm normal browsing, tabs, navigation, search, and downloads still work.

Use this Step 6 settings smoke test:

1. Open Settings from the toolbar and with `Ctrl+,`.
2. Enter a homepage domain such as `github.com`, save, and confirm Quartz normalizes it to `https://github.com/`.
3. Click **Home** and open a new tab; confirm both use the saved homepage.
4. Select Google, save, enter a search phrase in the address bar, and confirm the search uses Google. Repeat with Bing or DuckDuckGo.
5. Choose **Open blank page**, save, restart Quartz, and confirm the first tab opens `about:blank`.
6. Restart again and confirm the homepage, search engine, and startup choice persist.
7. Create test history, a bookmark, and a completed download record. Use each individual clear button and confirm the visible UI updates after confirmation.
8. Recreate test data, use **Clear all browsing data**, and confirm history, completed-download history, and bookmarks are cleared together.
9. Replace `settings.json` with invalid JSON and confirm Quartz starts safely with default settings.
10. Recheck tabs, bookmarks, History, Downloads, and existing keyboard shortcuts.

For download regression testing:

1. Click a link that downloads a moderately sized file. Confirm it is saved to the user's Downloads folder.
2. Confirm Quartz shows a download status in the browser status bar while the transfer starts and completes.
3. Confirm the completed file can be opened from Windows File Explorer.
4. Download the same filename twice and confirm Quartz chooses a non-conflicting save name.
5. Close and restart Quartz, then confirm the completed-download history remains available to Settings' clear-data controls.
6. Use Settings > **Clear downloads history** and confirm the stored completed-download records are removed without deleting downloaded files.
7. Recheck tabs, bookmarks, History, and `Ctrl+T`, `Ctrl+W`, `Ctrl+D`, `Ctrl+H`, `Ctrl+L`, `Ctrl+R`, `Alt+Left`, and `Alt+Right`.

The primary page checks are:

- `https://www.google.com`
- `https://www.youtube.com`
- `https://github.com`

Also confirm that Back and Forward enable only when available, the loading indicator follows the active tab, and closing the final tab closes Quartz.

## Project structure

```text
Quartz.sln
src/Quartz/
  App.xaml                  Shared Quartz palette and reusable control styles
  BrowserTab.cs             Per-tab WebView2 instance and browser state
  BrowserSettings.cs        Homepage and search settings
  MainWindow.xaml           Browser window layout
  MainWindow.xaml.cs        WebView2 event and command handling
  Models/
    Bookmark.cs             Stored bookmark data
    DownloadItem.cs         Live download state and progress
    DownloadRecord.cs       Persisted completed-download data
    HistoryEntry.cs         Stored page title, URL, and visit time
    BrowserPreferences.cs   Homepage, search engine, and startup preferences
  Services/
    AddressInterpreter.cs   URL and search-query interpretation
    BookmarkService.cs      Safe JSON loading, saving, and de-duplication
    DownloadService.cs      WebView2 download tracking and completed history
    HistoryService.cs       Capped, newest-first JSON history persistence
    SettingsService.cs      Safe JSON loading and saving for browser preferences
    ThemeManager.cs         Built-in palettes, accents, and live WPF resource updates
    NewTabPageBuilder.cs    Theme-aware local new-tab HTML and quick-link generation
  DownloadsWindow.xaml      Separate downloads manager window
  DownloadsWindow.xaml.cs   Download actions and private-window download display
  PermissionPromptWindow.xaml     Quartz site-permission dialog
  PermissionPromptWindow.xaml.cs  Allow/block permission decision handling
```

Version 0.10 intentionally does not include a custom color picker, real memory/CPU/network limiting, a full certificate interstitial, unsafe certificate bypass, per-site permission management, session restore, pause/resume, download retries, history search or grouping, bookmark folders or editing, bookmark import/export, accounts, sync, extensions, or advanced security controls.
